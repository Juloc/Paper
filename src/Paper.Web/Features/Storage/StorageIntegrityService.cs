using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.Storage;

public sealed class StorageIntegrityService(
    AppDbContext db,
    IStorageProvider storage,
    TimeProvider timeProvider,
    ILogger<StorageIntegrityService> logger)
{
    public const int MaximumReportedIssues = 50;

    public async Task<StorageIntegrityReport> CheckAsync(CancellationToken cancellationToken)
    {
        var documents = await db.Documents.AsNoTracking()
            .OrderBy(document => document.Id)
            .Select(document => new StorageIntegrityDocument(
                document.Id,
                document.Title,
                document.FilePath,
                document.FileSize))
            .ToListAsync(cancellationToken);

        var report = CheckDocuments(documents, storage.GetFileMetadata, timeProvider.GetUtcNow());
        if (report.Error is not null)
        {
            logger.LogWarning("Storage integrity check stopped after a storage error: {Error}", report.Error);
        }

        return report;
    }

    public static StorageIntegrityReport CheckDocuments(
        IReadOnlyList<StorageIntegrityDocument> documents,
        Func<string, StorageFileMetadata?> getFileMetadata,
        DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(getFileMetadata);

        var issues = new List<StorageIntegrityIssue>();
        var checkedCount = 0;
        var missingCount = 0;
        var sizeMismatchCount = 0;
        try
        {
            foreach (var document in documents)
            {
                checkedCount++;
                var metadata = getFileMetadata(document.FilePath);
                if (metadata is null)
                {
                    missingCount++;
                    if (issues.Count < MaximumReportedIssues)
                    {
                        issues.Add(new StorageIntegrityIssue(
                            document.Id,
                            document.Title,
                            document.FilePath,
                            StorageIntegrityIssueKind.Missing,
                            document.ExpectedSize,
                            null));
                    }
                }
                else if (metadata.Length != document.ExpectedSize)
                {
                    sizeMismatchCount++;
                    if (issues.Count < MaximumReportedIssues)
                    {
                        issues.Add(new StorageIntegrityIssue(
                            document.Id,
                            document.Title,
                            document.FilePath,
                            StorageIntegrityIssueKind.SizeMismatch,
                            document.ExpectedSize,
                            metadata.Length));
                    }
                }
            }

            return new StorageIntegrityReport(
                checkedAt,
                checkedCount,
                missingCount,
                sizeMismatchCount,
                issues,
                null);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new StorageIntegrityReport(checkedAt, checkedCount, missingCount, sizeMismatchCount, issues, exception.Message);
        }
    }
}

public sealed record StorageIntegrityDocument(long Id, string Title, string FilePath, long ExpectedSize);

public enum StorageIntegrityIssueKind
{
    Missing,
    SizeMismatch
}

public sealed record StorageIntegrityIssue(
    long DocumentId,
    string Title,
    string FilePath,
    StorageIntegrityIssueKind Kind,
    long ExpectedSize,
    long? ActualSize);

public sealed record StorageIntegrityReport(
    DateTimeOffset CheckedAt,
    int DocumentsChecked,
    int MissingFiles,
    int SizeMismatches,
    IReadOnlyList<StorageIntegrityIssue> Issues,
    string? Error)
{
    public bool IsHealthy => Error is null && MissingFiles == 0 && SizeMismatches == 0;
}
