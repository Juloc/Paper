using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.Processing;

namespace Paper.Web.Features.Documents;

public sealed class DocumentStore(AppDbContext db, TimeProvider timeProvider)
{
    public const int PageSize = 100;

    public async Task<DocumentPage> ListPageAsync(DocumentStatus status, int pageNumber, CancellationToken cancellationToken)
    {
        pageNumber = Math.Max(1, pageNumber);
        var totalCount = await db.Documents.CountAsync(document => document.Status == status, cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        pageNumber = Math.Min(pageNumber, pageCount);
        var documents = await Query(status)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);
        return new DocumentPage(documents, pageNumber, pageCount, totalCount);
    }

    public Task<int> CountAsync(DocumentStatus status, CancellationToken cancellationToken) =>
        db.Documents.CountAsync(document => document.Status == status, cancellationToken);

    public async Task<bool> SetInboxStatusAsync(long id, DocumentStatus status, CancellationToken cancellationToken)
    {
        if (status is not (DocumentStatus.Inbox or DocumentStatus.Deferred or DocumentStatus.Ignored))
        {
            return false;
        }

        var document = await db.Documents.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null || document.Status == DocumentStatus.Filed)
        {
            return false;
        }

        document.Status = status;
        document.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<DocumentDetails?> GetAsync(long id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking().AsSplitQuery()
            .Include(item => item.Tags).ThenInclude(item => item.Tag)
            .Include(item => item.Correspondent)
            .Include(item => item.DocumentType)
            .Include(item => item.ShelfFolder)
            .Include(item => item.CustomFields).ThenInclude(item => item.CustomField)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return document is null ? null : ToDetails(document);
    }

    public async Task<bool> QueueReanalysisAsync(long id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null)
        {
            return false;
        }

        if (await db.ProcessingJobs.AnyAsync(job =>
                job.DocumentId == id &&
                job.Type == ProcessingJobType.OcrAndAnalyze &&
                (job.State == ProcessingJobState.Pending || job.State == ProcessingJobState.Running), cancellationToken))
        {
            return true;
        }

        document.OcrStatus = OcrStatus.Pending;
        document.OcrError = null;
        db.ProcessingJobs.Add(new ProcessingJob
        {
            DocumentId = id,
            Type = ProcessingJobType.OcrAndAnalyze,
            State = ProcessingJobState.Pending,
            Priority = 20,
            Attempts = 0,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private IQueryable<DocumentListItem> Query(DocumentStatus status) =>
        db.Documents.AsNoTracking()
            .Where(document => document.Status == status)
            .OrderByDescending(document => document.UpdatedAt)
            .Select(document => new DocumentListItem(
                document.Id,
                document.Title,
                document.DocumentDate,
                document.OriginalFileName,
                document.FileSize,
                document.OcrStatus,
                document.Status,
                document.UpdatedAt,
                document.Tags.Select(documentTag => documentTag.Tag.Name).OrderBy(name => name).ToArray()));

    private static DocumentDetails ToDetails(Document document) => new(
        document.Id,
        document.Title,
        document.DocumentDate,
        document.OriginalFileName,
        document.FilePath,
        document.FileSize,
        document.Hash,
        document.OcrText,
        document.OcrStatus,
        document.OcrError,
        document.Status,
        document.CreatedAt,
        document.UpdatedAt,
        document.CorrespondentId,
        document.Correspondent?.Name,
        document.DocumentTypeId,
        document.DocumentType?.Name,
        document.ShelfFolderId,
        document.ShelfFolder?.RelativePath,
        document.Tags.Select(documentTag => documentTag.Tag.Name).OrderBy(name => name).ToArray(),
        document.CustomFields
            .OrderBy(value => value.CustomField.Name)
            .Select(value => new CustomFieldValueDetails(value.CustomFieldId, value.CustomField.Name, value.CustomField.Type, value.Value))
            .ToArray());
}

public sealed record DocumentListItem(long Id, string Title, DateOnly? DocumentDate, string OriginalFileName, long FileSize, OcrStatus OcrStatus, DocumentStatus Status, DateTime UpdatedAt, string[] Tags);

public sealed record DocumentPage(IReadOnlyList<DocumentListItem> Documents, int PageNumber, int PageCount, int TotalCount);

public sealed record CustomFieldValueDetails(long CustomFieldId, string Name, CustomFieldType Type, string Value);

public sealed record DocumentDetails(
    long Id,
    string Title,
    DateOnly? DocumentDate,
    string OriginalFileName,
    string FilePath,
    long FileSize,
    string Hash,
    string? OcrText,
    OcrStatus OcrStatus,
    string? OcrError,
    DocumentStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long? CorrespondentId,
    string? Correspondent,
    long? DocumentTypeId,
    string? DocumentType,
    long? ShelfFolderId,
    string? ShelfPath,
    string[] Tags,
    CustomFieldValueDetails[] CustomFields);
