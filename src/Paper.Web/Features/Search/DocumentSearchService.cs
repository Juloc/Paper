using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Documents;

namespace Paper.Web.Features.Search;

public sealed class DocumentSearchService(AppDbContext db)
{
    public const int PageSize = 100;

    public async Task<SearchPage> SearchAsync(SearchCriteria criteria, int pageNumber, CancellationToken cancellationToken)
    {
        var normalizedQuery = criteria.Query.Trim();
        if (normalizedQuery.Length > 200 || (normalizedQuery.Length == 0 && !criteria.HasFilters))
        {
            return new SearchPage([], 1, 1, 0);
        }

        pageNumber = Math.Max(1, pageNumber);
        var documents = BuildQuery(criteria, normalizedQuery);
        var totalCount = await documents.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        pageNumber = Math.Min(pageNumber, pageCount);
        var results = await documents
            .OrderByDescending(document => document.DocumentDate)
            .ThenByDescending(document => document.UpdatedAt)
            .ThenByDescending(document => document.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
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

        return new SearchPage(results, pageNumber, pageCount, totalCount);
    }

    private IQueryable<Document> BuildQuery(SearchCriteria criteria, string normalizedQuery)
    {
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

        if (criteria.CustomFieldId is not null)
        {
            if (string.IsNullOrWhiteSpace(criteria.CustomFieldValue))
            {
                documents = documents.Where(document => document.CustomFields.Any(field => field.CustomFieldId == criteria.CustomFieldId));
            }
            else
            {
                var value = criteria.CustomFieldValue.Trim();
                documents = documents.Where(document => document.CustomFields.Any(field =>
                    field.CustomFieldId == criteria.CustomFieldId && EF.Functions.ILike(field.Value, $"%{value}%")));
            }
        }

        return documents;
    }
}

public sealed record SearchPage(IReadOnlyList<DocumentListItem> Results, int PageNumber, int PageCount, int TotalCount);

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
        CustomFieldId is not null;
}
