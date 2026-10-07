namespace Paper.Web.Features.Documents;

public static class BatchUploadPolicy
{
    public const int MaximumFileCount = 50;
    public const long MaximumTotalSize = 500L * 1024 * 1024;
    public const int MaximumDisplayedErrors = 3;

    public static string? Validate(int fileCount, long totalSize)
    {
        if (fileCount <= 0)
        {
            return "Bitte wähle mindestens eine Datei aus.";
        }

        if (fileCount > MaximumFileCount)
        {
            return $"Du kannst höchstens {MaximumFileCount} Dateien auf einmal hochladen.";
        }

        if (totalSize > MaximumTotalSize)
        {
            return $"Die gesamte Upload-Größe darf höchstens {MaximumTotalSize / (1024 * 1024)} MB betragen.";
        }

        return null;
    }

    public static string SummarizeErrors(IReadOnlyList<string> errors)
    {
        if (errors.Count <= MaximumDisplayedErrors)
        {
            return string.Join(" | ", errors);
        }

        var remaining = errors.Count - MaximumDisplayedErrors;
        return $"{string.Join(" | ", errors.Take(MaximumDisplayedErrors))} | … und {remaining} weitere Datei(en)";
    }
}
