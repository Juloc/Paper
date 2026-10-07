namespace Paper.Web.Features.Storage;

public static class StoragePathPolicy
{
    public const int MaximumRelativePathLength = 500;

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

        var result = string.Join('/', segments);
        if (result.Length > MaximumRelativePathLength)
        {
            throw new ArgumentException($"Der Speicherpfad darf höchstens {MaximumRelativePathLength} Zeichen lang sein.", nameof(path));
        }

        return result;
    }

    public static string Combine(string folderPath, string fileName)
    {
        var normalizedFolder = NormalizeRelativePath(folderPath);
        var safeFileName = SanitizeFileName(fileName);
        var availableFileNameLength = MaximumRelativePathLength - normalizedFolder.Length - 1;
        if (availableFileNameLength <= 0)
        {
            throw new ArgumentException("Der Zielordner ist zu lang.", nameof(folderPath));
        }

        if (safeFileName.Length > availableFileNameLength)
        {
            var extension = Path.GetExtension(safeFileName);
            if (extension.Length >= availableFileNameLength)
            {
                safeFileName = safeFileName[..availableFileNameLength];
            }
            else
            {
                var stemLength = availableFileNameLength - extension.Length;
                safeFileName = safeFileName[..Math.Min(stemLength, safeFileName.Length - extension.Length)] + extension;
            }
        }

        return $"{normalizedFolder}/{safeFileName}";
    }

    public static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(character => character < 32 ||
                                 invalidCharacters.Contains(character) ||
                                 character is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : character)
            .ToArray());
        sanitized = string.Join(' ', sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "Dokument";
        }

        const int maximumFileNameLength = 220;
        if (sanitized.Length <= maximumFileNameLength)
        {
            return sanitized;
        }

        var extension = Path.GetExtension(sanitized);
        var stemLength = Math.Max(1, maximumFileNameLength - extension.Length);
        return sanitized[..Math.Min(stemLength, sanitized.Length - extension.Length)] + extension;
    }
}
