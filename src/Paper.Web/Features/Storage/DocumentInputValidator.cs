namespace Paper.Web.Features.Storage;

public static class DocumentInputValidator
{
    public const long MaximumFileSize = 50 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff"
    };

    public static bool TryValidate(string fileName, string? contentType, long length, ReadOnlySpan<byte> header, out string extension, out string error)
    {
        extension = Path.GetExtension(fileName).ToLowerInvariant();
        error = "";
        if (!AllowedTypes.ContainsKey(extension))
        {
            error = "Dieses Dateiformat wird nicht unterstützt.";
            return false;
        }

        if (length <= 0 || length > MaximumFileSize)
        {
            error = "Die Datei ist leer oder größer als 50 MB.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(contentType) && !string.Equals(contentType, AllowedTypes[extension], StringComparison.OrdinalIgnoreCase))
        {
            error = "Der erkannte Dateityp stimmt nicht mit der Dateiendung überein.";
            return false;
        }

        if (!MatchesSignature(extension, header))
        {
            error = "Der Dateiinhalt passt nicht zum angegebenen Dateityp.";
            return false;
        }

        return true;
    }

    private static bool MatchesSignature(string extension, ReadOnlySpan<byte> header) => extension switch
    {
        ".pdf" => header.Length >= 5 && header[..5].SequenceEqual("%PDF-"u8),
        ".jpg" or ".jpeg" => header.Length >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff,
        ".png" => header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
        ".tif" or ".tiff" => header.Length >= 4 && (header[..4].SequenceEqual(new byte[] { 0x49, 0x49, 0x2a, 0x00 }) || header[..4].SequenceEqual(new byte[] { 0x4d, 0x4d, 0x00, 0x2a })),
        _ => false
    };
}
