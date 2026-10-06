using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Documents;

namespace Paper.Web.Features.Search;

public sealed class DocumentSearchService(AppDbContext db)
{
    public Task<List<DocumentListItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length is 0 or > 200)
        {
            return Task.FromResult(new List<DocumentListItem>());
        }

        return db.Documents.AsNoTracking()
            .Where(document => document.SearchVector.Matches(EF.Functions.PlainToTsQuery("german", normalizedQuery)))
            .OrderByDescending(document => document.UpdatedAt)
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
