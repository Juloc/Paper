using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using System.Security.Cryptography;

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
                document.FileSize,
                document.Hash))
            .ToListAsync(cancellationToken);

        var report = await CheckDocumentsAsync(
            documents,
            async document =>
            {
                var metadata = storage.GetFileMetadata(document.FilePath);
                if (metadata is null)
                {
                    return null;
                }

                await using var stream = storage.OpenRead(document.FilePath);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                }

                return new StorageFileVerification(
                    metadata.Length,
                    Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
            },
            timeProvider.GetUtcNow(),
            cancellationToken);
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

        return CheckDocumentsAsync(
                documents,
                document => Task.FromResult(
                    getFileMetadata(document.FilePath) is { } metadata
                        ? new StorageFileVerification(metadata.Length, null)
                        : null),
                checkedAt,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public static async Task<StorageIntegrityReport> CheckDocumentsAsync(
        IReadOnlyList<StorageIntegrityDocument> documents,
        Func<StorageIntegrityDocument, Task<StorageFileVerification?>> getVerification,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(getVerification);

        var issues = new List<StorageIntegrityIssue>();
        var checkedCount = 0;
        var missingCount = 0;
        var sizeMismatchCount = 0;
        var hashMismatchCount = 0;
        try
        {
            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                checkedCount++;
                var verification = await getVerification(document);
                if (verification is null)
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
                else if (verification.Length != document.ExpectedSize)
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
                            verification.Length));
                    }
                }
                else if (!string.IsNullOrWhiteSpace(document.ExpectedHash) &&
                         !string.Equals(document.ExpectedHash, verification.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    hashMismatchCount++;
                    if (issues.Count < MaximumReportedIssues)
                    {
                        issues.Add(new StorageIntegrityIssue(
                            document.Id,
                            document.Title,
                            document.FilePath,
                            StorageIntegrityIssueKind.HashMismatch,
                            document.ExpectedSize,
                            verification.Length));
                    }
                }
            }

            return new StorageIntegrityReport(
                checkedAt,
                checkedCount,
                missingCount,
                sizeMismatchCount,
                hashMismatchCount,
                issues,
                null);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new StorageIntegrityReport(checkedAt, checkedCount, missingCount, sizeMismatchCount, hashMismatchCount, issues, exception.Message);
        }
    }
}

public sealed record StorageIntegrityDocument(long Id, string Title, string FilePath, long ExpectedSize, string? ExpectedHash = null);

public sealed record StorageFileVerification(long Length, string? Hash);

public enum StorageIntegrityIssueKind
{
    Missing,
    SizeMismatch,
    HashMismatch
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
    int HashMismatches,
    IReadOnlyList<StorageIntegrityIssue> Issues,
    string? Error)
{
    public bool IsHealthy => Error is null && MissingFiles == 0 && SizeMismatches == 0 && HashMismatches == 0;
}
