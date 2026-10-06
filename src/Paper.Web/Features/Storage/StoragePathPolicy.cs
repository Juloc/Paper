namespace Paper.Web.Features.Storage;

public static class StoragePathPolicy
{
    public static string CreateInboxPath(string hash, string originalFileName)
    {
        var safeName = SanitizeFileName(originalFileName);
        return Combine("inbox", $"{hash[..12]} {safeName}");
    }

    public static string CreateShelfFileName(DateOnly? documentDate, string title, string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var datePart = documentDate?.ToString("yyyy-MM-dd") ?? "undated";
        var titlePart = SanitizeFileName(title);
        return SanitizeFileName($"{datePart} {titlePart}{extension}");
    }

    public static string NormalizeFolderPath(string path)
    {
        var normalized = NormalizeRelativePath(path);
        if (normalized.Equals("inbox", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("inbox/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Der Inbox-Ordner ist kein Regalordner.", nameof(path));
        }

        return normalized;
    }

    public static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new ArgumentException("Ein relativer Speicherpfad ist erforderlich.", nameof(path));
        }

        var normalized = path.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            segments.Any(segment => segment is "." or ".." || segment.Contains(':') || segment.Any(character => character < 32 || Path.GetInvalidFileNameChars().Contains(character))))
        {
            throw new ArgumentException("Der Speicherpfad ist ungültig.", nameof(path));
        }

        return string.Join('/', segments);
    }

    public static string Combine(string folderPath, string fileName)
    {
        var normalizedFolder = NormalizeRelativePath(folderPath);
        var safeFileName = SanitizeFileName(fileName);
        return $"{normalizedFolder}/{safeFileName}";
    }

    public static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(character => character < 32 || invalidCharacters.Contains(character) || character is '/' or '\\' ? '_' : character)
            .ToArray());
        sanitized = string.Join(' ', sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(sanitized) ? "Dokument" : sanitized[..Math.Min(220, sanitized.Length)];
    }
}
