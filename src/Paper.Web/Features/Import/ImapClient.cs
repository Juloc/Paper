using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;

namespace Paper.Web.Features.Import;

public sealed class ImapClient : IAsyncDisposable
{
    private static readonly Regex LiteralPattern = new(@"\{(?<length>\d+)\}$", RegexOptions.Compiled);
    private readonly MailAccountOptions options;
    private readonly CancellationToken cancellationToken;
    private TcpClient? tcpClient;
    private Stream? stream;
    private int commandNumber;

    public ImapClient(MailAccountOptions options, CancellationToken cancellationToken)
    {
        this.options = options;
        this.cancellationToken = cancellationToken;
    }

    public async Task ConnectAsync()
    {
        tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(options.Host, options.Port, cancellationToken);
        stream = tcpClient.GetStream();
        if (options.UseSsl)
        {
            var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = options.Host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, cancellationToken);
            stream = ssl;
        }

        var greeting = await ReadLineAsync();
        if (greeting is null || (!greeting.StartsWith("* OK", StringComparison.OrdinalIgnoreCase) &&
                                 !greeting.StartsWith("* PREAUTH", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Der IMAP-Server hat keine gültige Begrüßung gesendet.");
        }

        await ExecuteAsync($"LOGIN {Quote(options.Username)} {Quote(options.Password)}");
        await ExecuteAsync($"SELECT {Quote(options.Folder)}");
    }

    public async Task<IReadOnlyList<long>> SearchAsync(long afterUid, int maximum, bool onlyUnread)
    {
        var criteria = onlyUnread ? "UNSEEN " : "";
        var response = await ExecuteAsync($"UID SEARCH {criteria}UID {Math.Max(1, afterUid)}:*");
        var searchLine = response.Lines.FirstOrDefault(line => line.StartsWith("* SEARCH", StringComparison.OrdinalIgnoreCase));
        if (searchLine is null)
        {
            return [];
        }

        return searchLine[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var uid) ? uid : 0)
            .Where(uid => uid > 0)
            .OrderBy(uid => uid)
            .Take(maximum)
            .ToArray();
    }

    public async Task<byte[]> FetchMessageAsync(long uid)
    {
        var response = await ExecuteAsync($"UID FETCH {uid} (BODY.PEEK[])", captureLiterals: true);
        var literal = response.Literals.FirstOrDefault();
        return literal ?? throw new InvalidDataException($"IMAP UID {uid} enthielt keine Nachricht.");
    }

    public async Task MarkSeenAsync(long uid)
    {
        await ExecuteAsync($"UID STORE {uid} +FLAGS.SILENT (\\Seen)");
    }

    public async ValueTask DisposeAsync()
    {
        if (stream is not null)
        {
            try
            {
                await ExecuteAsync("LOGOUT");
            }
            catch
            {
                // The connection may already have been closed by the server.
            }

            await stream.DisposeAsync();
        }

        tcpClient?.Dispose();
    }

    private async Task<ImapResponse> ExecuteAsync(string command, bool captureLiterals = false)
    {
        if (stream is null)
        {
            throw new InvalidOperationException("IMAP connection is not open.");
        }

        var tag = $"A{++commandNumber:0000}";
        var bytes = Encoding.UTF8.GetBytes($"{tag} {command}\r\n");
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var lines = new List<string>();
        var literals = new List<byte[]>();
        while (true)
        {
            var lineBytes = await ReadLineBytesAsync();
            var line = Encoding.UTF8.GetString(lineBytes);
            var literalMatch = LiteralPattern.Match(line);
            if (captureLiterals && literalMatch.Success && int.TryParse(literalMatch.Groups["length"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var length))
            {
                if (length > 100 * 1024 * 1024)
                {
                    throw new InvalidDataException("Die IMAP-Nachricht ist größer als 100 MB.");
                }

                literals.Add(await ReadExactAsync(length));
            }

            lines.Add(line);
            if (!line.StartsWith(tag + " ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!line.Contains(" OK", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"IMAP command failed: {command.Split(' ', 2)[0]}.");
            }

            return new ImapResponse(lines, literals);
        }
    }

    private async Task<string?> ReadLineAsync()
    {
        var bytes = await ReadLineBytesAsync(throwOnEnd: false);
        return bytes.Length == 0 ? null : Encoding.UTF8.GetString(bytes);
    }

    private async Task<byte[]> ReadLineBytesAsync(bool throwOnEnd = true)
    {
        if (stream is null)
        {
            throw new InvalidOperationException("IMAP connection is not open.");
        }

        using var line = new MemoryStream();
        var one = new byte[1];
        while (line.Length <= 1024 * 1024)
        {
            var count = await stream.ReadAsync(one, cancellationToken);
            if (count == 0)
            {
                if (!throwOnEnd && line.Length == 0)
                {
                    return [];
                }

                throw new EndOfStreamException("Die IMAP-Verbindung wurde unerwartet beendet.");
            }

            if (one[0] == (byte)'\n')
            {
                var result = line.ToArray();
                return result.Length > 0 && result[^1] == (byte)'\r' ? result[..^1] : result;
            }

            line.WriteByte(one[0]);
        }

        throw new InvalidDataException("Die IMAP-Antwortzeile ist zu lang.");
    }

    private async Task<byte[]> ReadExactAsync(int length)
    {
        if (stream is null)
        {
            throw new InvalidOperationException("IMAP connection is not open.");
        }

        var result = new byte[length];
        var offset = 0;
        while (offset < result.Length)
        {
            var read = await stream.ReadAsync(result.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Die IMAP-Nachricht wurde unvollständig übertragen.");
            }

            offset += read;
        }

        return result;
    }

    private static string Quote(string value)
    {
        if (value.Contains('\r') || value.Contains('\n'))
        {
            throw new InvalidDataException("IMAP-Konfigurationswerte dürfen keine Zeilenumbrüche enthalten.");
        }

        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private sealed record ImapResponse(IReadOnlyList<string> Lines, IReadOnlyList<byte[]> Literals);
}
