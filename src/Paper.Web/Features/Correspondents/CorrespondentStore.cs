using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features;

namespace Paper.Web.Features.Correspondents;

public sealed class CorrespondentStore(AppDbContext db)
{
    public Task<List<CorrespondentOption>> ListAsync(CancellationToken cancellationToken) =>
        db.Correspondents.AsNoTracking()
            .OrderBy(correspondent => correspondent.Name)
            .Select(correspondent => new CorrespondentOption(correspondent.Id, correspondent.Name))
            .ToListAsync(cancellationToken);

    public async Task<Correspondent?> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length is 0 or > 200)
        {
            return null;
        }

        var comparisonName = normalizedName.ToLowerInvariant();
        var existing = await db.Correspondents.SingleOrDefaultAsync(item => item.NameKey == comparisonName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var correspondent = new Correspondent { Name = normalizedName };
        db.Correspondents.Add(correspondent);
        await db.SaveChangesAsync(cancellationToken);
        return correspondent;
    }

    public async Task<CatalogDeleteResult> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var correspondent = await db.Correspondents.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (correspondent is null)
        {
            return CatalogDeleteResult.Missing;
        }

        if (await db.Documents.AnyAsync(item => item.CorrespondentId == id, cancellationToken) ||
            await db.AnalysisRules.AnyAsync(item => item.CorrespondentId == id, cancellationToken))
        {
            return CatalogDeleteResult.Used;
        }

        db.Correspondents.Remove(correspondent);
        await db.SaveChangesAsync(cancellationToken);
        return CatalogDeleteResult.Success;
    }
}

public sealed record CorrespondentOption(long Id, string Name);
