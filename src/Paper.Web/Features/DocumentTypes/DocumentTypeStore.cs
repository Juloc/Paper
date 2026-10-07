using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features;

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
        var existing = await db.DocumentTypes.SingleOrDefaultAsync(item => item.NameKey == comparisonName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var documentType = new DocumentType { Name = normalizedName };
        db.DocumentTypes.Add(documentType);
        await db.SaveChangesAsync(cancellationToken);
        return documentType;
    }

    public async Task<CatalogDeleteResult> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var documentType = await db.DocumentTypes.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (documentType is null)
        {
            return CatalogDeleteResult.Missing;
        }

        if (await db.Documents.AnyAsync(item => item.DocumentTypeId == id, cancellationToken) ||
            await db.AnalysisRules.AnyAsync(item => item.DocumentTypeId == id, cancellationToken))
        {
            return CatalogDeleteResult.Used;
        }

        db.DocumentTypes.Remove(documentType);
        await db.SaveChangesAsync(cancellationToken);
        return CatalogDeleteResult.Success;
    }
}

public sealed record DocumentTypeOption(long Id, string Name);
