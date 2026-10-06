using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Documents;

namespace Paper.Web.Features.Search;

public sealed class DocumentSearchService(AppDbContext db)
{
    public Task<List<DocumentListItem>> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken)
    {
        var normalizedQuery = criteria.Query.Trim();
        if (normalizedQuery.Length > 200 || (normalizedQuery.Length == 0 && !criteria.HasFilters))
        {
            return Task.FromResult(new List<DocumentListItem>());
        }

        var documents = db.Documents.AsNoTracking().AsQueryable();
        if (normalizedQuery.Length > 0)
        {
            documents = documents.Where(document =>
                document.SearchVector.Matches(EF.Functions.PlainToTsQuery("german", normalizedQuery)));
        }

        if (criteria.CorrespondentId is not null)
        {
            documents = documents.Where(document => document.CorrespondentId == criteria.CorrespondentId);
        }

        if (criteria.DocumentTypeId is not null)
        {
            documents = documents.Where(document => document.DocumentTypeId == criteria.DocumentTypeId);
        }

        if (criteria.ShelfFolderId is not null)
        {
            documents = documents.Where(document => document.ShelfFolderId == criteria.ShelfFolderId);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Tag))
        {
            var tag = criteria.Tag.Trim().ToLowerInvariant();
            documents = documents.Where(document => document.Tags.Any(link => link.Tag.Name == tag));
        }

        if (criteria.FromDate is not null)
        {
            documents = documents.Where(document => document.DocumentDate >= criteria.FromDate);
        }

        if (criteria.ToDate is not null)
        {
            documents = documents.Where(document => document.DocumentDate <= criteria.ToDate);
        }

        if (criteria.CustomFieldId is not null && !string.IsNullOrWhiteSpace(criteria.CustomFieldValue))
        {
            var value = criteria.CustomFieldValue.Trim();
            documents = documents.Where(document => document.CustomFields.Any(field =>
                field.CustomFieldId == criteria.CustomFieldId && EF.Functions.ILike(field.Value, $"%{value}%")));
        }

        return documents
            .OrderByDescending(document => document.DocumentDate)
            .ThenByDescending(document => document.UpdatedAt)
            .Take(100)
            .Select(document => new DocumentListItem(
                document.Id,
                document.Title,
                document.DocumentDate,
                document.OriginalFileName,
                document.FileSize,
                document.OcrStatus,
                document.Status,
                document.UpdatedAt,
                document.Tags.Select(documentTag => documentTag.Tag.Name).OrderBy(name => name).ToArray()))
            .ToListAsync(cancellationToken);
    }
}

public sealed record SearchCriteria(
    string Query,
    long? CorrespondentId,
    long? DocumentTypeId,
    long? ShelfFolderId,
    string? Tag,
    DateOnly? FromDate,
    DateOnly? ToDate,
    long? CustomFieldId,
    string? CustomFieldValue)
{
    public bool HasFilters =>
        CorrespondentId is not null ||
        DocumentTypeId is not null ||
        ShelfFolderId is not null ||
        !string.IsNullOrWhiteSpace(Tag) ||
        FromDate is not null ||
        ToDate is not null ||
        CustomFieldId is not null && !string.IsNullOrWhiteSpace(CustomFieldValue);
}
