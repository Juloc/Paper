using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Tags;

namespace Paper.Web.Features.Documents;

public sealed class DocumentStore(AppDbContext db, TimeProvider timeProvider)
{
    public Task<List<DocumentListItem>> ListInboxAsync(CancellationToken cancellationToken) =>
        Query(DocumentStatus.Inbox).ToListAsync(cancellationToken);

    public Task<List<DocumentListItem>> ListArchivedAsync(CancellationToken cancellationToken) =>
        Query(DocumentStatus.Archived).ToListAsync(cancellationToken);

    public async Task<DocumentDetails?> GetAsync(long id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking()
            .Include(item => item.Tags)
            .ThenInclude(item => item.Tag)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return document is null ? null : ToDetails(document);
    }

    public async Task<bool> UpdateAsync(long id, string title, DateOnly? documentDate, string? tags, CancellationToken cancellationToken)
    {
        var document = await db.Documents.Include(item => item.Tags).ThenInclude(item => item.Tag).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null || string.IsNullOrWhiteSpace(title) || title.Trim().Length > 300)
        {
            return false;
        }

        document.Title = title.Trim();
        document.DocumentDate = documentDate;
        var tagNames = (tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.ToLowerInvariant())
            .Where(value => value.Length <= 80)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var documentTag in document.Tags.Where(documentTag => !tagNames.Contains(documentTag.Tag.Name)).ToArray())
        {
            db.DocumentTags.Remove(documentTag);
            document.Tags.Remove(documentTag);
        }

        var currentNames = document.Tags.Select(documentTag => documentTag.Tag.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingTags = await db.Tags.Where(tag => tagNames.Contains(tag.Name)).ToDictionaryAsync(tag => tag.Name, cancellationToken);
        foreach (var name in tagNames)
        {
            if (currentNames.Contains(name))
            {
                continue;
            }

            if (!existingTags.TryGetValue(name, out var tag))
            {
                tag = new Tag { Name = name };
                db.Tags.Add(tag);
            }

            document.Tags.Add(new DocumentTag { Document = document, Tag = tag });
        }

        document.SearchText = TagStore.BuildSearchText(document);
        document.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ArchiveAsync(long id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null)
        {
            return false;
        }

        document.Status = DocumentStatus.Archived;
        document.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
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
        document.Tags.Select(documentTag => documentTag.Tag.Name).OrderBy(name => name).ToArray());
}

public sealed record DocumentListItem(long Id, string Title, DateOnly? DocumentDate, string OriginalFileName, long FileSize, OcrStatus OcrStatus, DocumentStatus Status, DateTime UpdatedAt, string[] Tags);

public sealed record DocumentDetails(long Id, string Title, DateOnly? DocumentDate, string OriginalFileName, string FilePath, long FileSize, string Hash, string? OcrText, OcrStatus OcrStatus, string? OcrError, DocumentStatus Status, DateTime CreatedAt, DateTime UpdatedAt, string[] Tags);
