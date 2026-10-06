using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Paper.Web.Features.Import;

public sealed class EmailAttachmentExtractor
{
    private static readonly Regex ParameterPattern = new(
        @"(?:^|;)\s*(?<name>[A-Za-z0-9_-]+\*?)\s*=\s*(?:""(?<quoted>[^""]*)""|(?<plain>[^;\s]*))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EncodedWordPattern = new(
        @"=\?(?<charset>[^?]+)\?(?<encoding>[bBqQ])\?(?<value>[^?]*)\?=",
        RegexOptions.Compiled);

    public const long MaximumMessageSize = 100L * 1024 * 1024;

    public async Task<IReadOnlyList<EmailAttachment>> ExtractAsync(Stream source, CancellationToken cancellationToken)
    {
        await using var content = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (content.Length + read > MaximumMessageSize)
            {
                throw new InvalidDataException("Die E-Mail ist größer als 100 MB.");
            }

            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Extract(content.ToArray());
    }

    public IReadOnlyList<EmailAttachment> Extract(byte[] message)
    {
        var attachments = new List<EmailAttachment>();
        ParseEntity(message, 0, attachments);
        return attachments;
    }

    private static void ParseEntity(byte[] entity, int depth, List<EmailAttachment> attachments)
    {
        if (depth > 12)
        {
            return;
        }

        var separator = FindHeaderSeparator(entity);
        if (separator is null)
        {
            return;
        }

        var headers = ParseHeaders(entity.AsSpan(0, separator.Value.HeaderLength));
        var body = entity.AsSpan(separator.Value.BodyOffset).ToArray();
        var contentType = headers.GetValueOrDefault("content-type") ?? "text/plain";
        var mediaType = contentType.Split(';', 2)[0].Trim();
        if (mediaType.Equals("multipart/mixed", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("multipart/alternative", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("multipart/related", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = GetParameter(contentType, "boundary");
            if (!string.IsNullOrWhiteSpace(boundary))
            {
                foreach (var part in SplitMultipart(body, boundary))
                {
                    ParseEntity(part, depth + 1, attachments);
                }
            }

            return;
        }

        var disposition = headers.GetValueOrDefault("content-disposition") ?? "";
        var fileName = DecodeHeader(GetParameter(disposition, "filename") ?? GetParameter(contentType, "name"));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var transferEncoding = headers.GetValueOrDefault("content-transfer-encoding") ?? "";
        var decoded = DecodeBody(body, transferEncoding);
        if (decoded.Length > 0)
        {
            attachments.Add(new EmailAttachment(fileName, mediaType, decoded));
        }
    }

    private static IEnumerable<byte[]> SplitMultipart(byte[] body, string boundary)
    {
        var marker = Encoding.ASCII.GetBytes("--" + boundary.Trim('"'));
        var start = FindBytes(body, marker, 0);
        while (start >= 0)
        {
            var contentStart = start + marker.Length;
            if (contentStart + 1 < body.Length && body[contentStart] == (byte)'-' && body[contentStart + 1] == (byte)'-')
            {
                yield break;
            }

            contentStart = SkipLineBreak(body, contentStart);
            var next = FindBytes(body, marker, contentStart);
            var contentEnd = next >= 0 ? next : body.Length;
            while (contentEnd > contentStart && (body[contentEnd - 1] == (byte)'\r' || body[contentEnd - 1] == (byte)'\n'))
            {
                contentEnd--;
            }

            if (contentEnd > contentStart)
            {
                yield return body[contentStart..contentEnd];
            }

            if (next < 0)
            {
                yield break;
            }

            start = next;
        }
    }

    private static byte[] DecodeBody(byte[] body, string transferEncoding)
    {
        if (transferEncoding.Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            var text = Encoding.ASCII.GetString(body);
            text = new string(text.Where(character => !char.IsWhiteSpace(character)).ToArray());
            try
            {
                return Convert.FromBase64String(text);
            }
            catch (FormatException)
            {
                return [];
            }
        }

        if (transferEncoding.Contains("quoted-printable", StringComparison.OrdinalIgnoreCase))
        {
            var output = new List<byte>(body.Length);
            for (var index = 0; index < body.Length; index++)
            {
                if (body[index] == (byte)'=' && index + 2 < body.Length)
                {
                    if (body[index + 1] is (byte)'\r' or (byte)'\n')
                    {
                        index++;
                        if (body[index] == (byte)'\r' && index + 1 < body.Length && body[index + 1] == (byte)'\n')
                        {
                            index++;
                        }

                        continue;
                    }

                    if (byte.TryParse(Encoding.ASCII.GetString(body, index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                    {
                        output.Add(value);
                        index += 2;
                        continue;
                    }
                }

                output.Add(body[index]);
            }

            return output.ToArray();
        }

        return body;
    }

    private static Dictionary<string, string> ParseHeaders(ReadOnlySpan<byte> rawHeaders)
    {
        var text = Encoding.Latin1.GetString(rawHeaders);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentName = null;
        var currentValue = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            var normalized = line.TrimEnd('\r');
            if (normalized.StartsWith(' ') || normalized.StartsWith('\t'))
            {
                currentValue.Append(' ').Append(normalized.Trim());
                continue;
            }

            if (currentName is not null)
            {
                headers[currentName] = currentValue.ToString();
            }

            var separator = normalized.IndexOf(':');
            if (separator <= 0)
            {
                currentName = null;
                currentValue.Clear();
                continue;
            }

            currentName = normalized[..separator].Trim();
            currentValue.Clear();
            currentValue.Append(normalized[(separator + 1)..].Trim());
        }

        if (currentName is not null)
        {
            headers[currentName] = currentValue.ToString();
        }

        return headers;
    }

    private static string? GetParameter(string? header, string name)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        foreach (Match match in ParameterPattern.Matches(header))
        {
            if (string.Equals(match.Groups["name"].Value.TrimEnd('*'), name, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["plain"].Value;
            }
        }

        return null;
    }

    private static string DecodeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return EncodedWordPattern.Replace(value, match =>
        {
            try
            {
                var bytes = match.Groups["encoding"].Value.Equals("B", StringComparison.OrdinalIgnoreCase)
                    ? Convert.FromBase64String(match.Groups["value"].Value)
                    : DecodeQuotedHeader(match.Groups["value"].Value);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return match.Value;
            }
        });
    }

    private static byte[] DecodeQuotedHeader(string value) =>
        Encoding.ASCII.GetBytes(value.Replace('_', ' '));

    private static (int HeaderLength, int BodyOffset)? FindHeaderSeparator(byte[] data)
    {
        for (var index = 0; index < data.Length - 1; index++)
        {
            if (data[index] == '\n' && data[index + 1] == '\n')
            {
                return (index, index + 2);
            }

            if (index < data.Length - 3 &&
                data[index] == '\r' && data[index + 1] == '\n' &&
                data[index + 2] == '\r' && data[index + 3] == '\n')
            {
                return (index, index + 4);
            }
        }

        return null;
    }

    private static int SkipLineBreak(byte[] data, int index)
    {
        if (index + 1 < data.Length && data[index] == '\r' && data[index + 1] == '\n')
        {
            return index + 2;
        }

        return index < data.Length && data[index] == '\n' ? index + 1 : index;
    }

    private static int FindBytes(byte[] data, byte[] needle, int start)
    {
        for (var index = start; index <= data.Length - needle.Length; index++)
        {
            if (data.AsSpan(index, needle.Length).SequenceEqual(needle))
            {
                return index;
            }
        }

        return -1;
    }
}

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);
