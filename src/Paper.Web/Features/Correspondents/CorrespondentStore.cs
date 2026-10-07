using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

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
}

public sealed record CorrespondentOption(long Id, string Name);
