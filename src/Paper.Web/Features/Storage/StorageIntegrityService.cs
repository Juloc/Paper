using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.Storage;

public sealed class StorageIntegrityService(
    AppDbContext db,
    IStorageProvider storage,
    TimeProvider timeProvider,
    ILogger<StorageIntegrityService> logger)
{
    public const int MaximumReportedMissingFiles = 50;

    public async Task<StorageIntegrityReport> CheckAsync(CancellationToken cancellationToken)
    {
        var documents = await db.Documents.AsNoTracking()
            .OrderBy(document => document.Id)
            .Select(document => new StorageIntegrityDocument(
                document.Id,
                document.Title,
                document.FilePath))
            .ToListAsync(cancellationToken);

        var report = CheckDocuments(documents, storage.FileExists, timeProvider.GetUtcNow());
        if (report.Error is not null)
        {
            logger.LogWarning("Storage integrity check stopped after a storage error: {Error}", report.Error);
        }

        return report;
    }

    public static StorageIntegrityReport CheckDocuments(
        IReadOnlyList<StorageIntegrityDocument> documents,
        Func<string, bool> fileExists,
        DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(fileExists);

        var missing = new List<StorageIntegrityIssue>();
        var checkedCount = 0;
        var missingCount = 0;
        try
        {
            foreach (var document in documents)
            {
                checkedCount++;
                if (!fileExists(document.FilePath))
                {
                    missingCount++;
                    if (missing.Count < MaximumReportedMissingFiles)
                    {
                        missing.Add(new StorageIntegrityIssue(document.Id, document.Title, document.FilePath));
                    }
                }
            }

            return new StorageIntegrityReport(
                checkedAt,
                checkedCount,
                missingCount,
                missing,
                null);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new StorageIntegrityReport(checkedAt, checkedCount, missingCount, missing, exception.Message);
        }
    }
}

public sealed record StorageIntegrityDocument(long Id, string Title, string FilePath);

public sealed record StorageIntegrityIssue(long DocumentId, string Title, string FilePath);

public sealed record StorageIntegrityReport(
    DateTimeOffset CheckedAt,
    int DocumentsChecked,
    int MissingFiles,
    IReadOnlyList<StorageIntegrityIssue> MissingDocuments,
    string? Error)
{
    public bool IsHealthy => Error is null && MissingFiles == 0;
}
