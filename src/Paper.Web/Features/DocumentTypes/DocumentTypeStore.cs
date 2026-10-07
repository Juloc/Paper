using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.DocumentTypes;

public sealed class DocumentTypeStore(AppDbContext db)
{
    public Task<List<DocumentTypeOption>> ListAsync(CancellationToken cancellationToken) =>
        db.DocumentTypes.AsNoTracking()
            .OrderBy(documentType => documentType.Name)
            .Select(documentType => new DocumentTypeOption(documentType.Id, documentType.Name))
            .ToListAsync(cancellationToken);

    public async Task<DocumentType?> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length is 0 or > 120)
        {
            return null;
        }

        var comparisonName = normalizedName.ToLowerInvariant();
        var existing = await db.DocumentTypes.SingleOrDefaultAsync(item => item.Name.ToLower() == comparisonName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var documentType = new DocumentType { Name = normalizedName };
        db.DocumentTypes.Add(documentType);
        await db.SaveChangesAsync(cancellationToken);
        return documentType;
    }
}

public sealed record DocumentTypeOption(long Id, string Name);
