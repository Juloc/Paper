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
        if (criteria.ShelfFolderId is not null)
        {
            var shelfPath = await db.ShelfFolders
                .Where(folder => folder.Id == criteria.ShelfFolderId)
                .Select(folder => folder.RelativePath)
                .SingleOrDefaultAsync(cancellationToken);
            if (shelfPath is null)
            {
                return new SearchPage([], 1, 1, 0);
            }

            documents = documents.Where(document => document.ShelfFolder != null &&
                (document.ShelfFolder.RelativePath == shelfPath || document.ShelfFolder.RelativePath.StartsWith(shelfPath + "/")));
        }

        var totalCount = await documents.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        pageNumber = Math.Min(pageNumber, pageCount);
        var results = await documents
            .OrderBy(document => document.DocumentDate == null)
            .ThenByDescending(document => document.DocumentDate)
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
            var fullTextQuery = EF.Functions.PlainToTsQuery("german", normalizedQuery);
            if (SearchQueryPolicy.NeedsLiteralFallback(normalizedQuery))
            {
                var literalPattern = SearchQueryPolicy.ToLikePattern(normalizedQuery);
                documents = documents.Where(document =>
                    document.SearchVector.Matches(fullTextQuery) ||
                    EF.Functions.ILike(document.SearchText, literalPattern, "\\"));
            }
            else
            {
                documents = documents.Where(document => document.SearchVector.Matches(fullTextQuery));
            }
        }

        if (criteria.CorrespondentId is not null)
        {
            documents = documents.Where(document => document.CorrespondentId == criteria.CorrespondentId);
        }

        if (criteria.DocumentTypeId is not null)
        {
            documents = documents.Where(document => document.DocumentTypeId == criteria.DocumentTypeId);
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
                    field.CustomFieldId == criteria.CustomFieldId &&
                    EF.Functions.ILike(field.Value, SearchQueryPolicy.ToLikePattern(value), "\\")));
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

public static class SearchQueryPolicy
{
    public static bool NeedsLiteralFallback(string query) => query.Any(character => char.IsDigit(character) || !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character));

    public static string ToLikePattern(string query) => $"%{query.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
}
