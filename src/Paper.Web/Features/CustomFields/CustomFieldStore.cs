using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features;

namespace Paper.Web.Features.CustomFields;

public sealed class CustomFieldStore(AppDbContext db)
{
    public Task<List<CustomFieldOption>> ListAsync(CancellationToken cancellationToken) =>
        db.CustomFields.AsNoTracking()
            .OrderBy(field => field.Name)
            .Select(field => new CustomFieldOption(field.Id, field.Name, field.Type))
            .ToListAsync(cancellationToken);

    public async Task<CustomField?> CreateAsync(string name, CustomFieldType type, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length is 0 or > 120 || !Enum.IsDefined(type))
        {
            return null;
        }

        var comparisonName = normalizedName.ToLowerInvariant();
        var existing = await db.CustomFields.SingleOrDefaultAsync(item => item.NameKey == comparisonName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var field = new CustomField { Name = normalizedName, Type = type };
        db.CustomFields.Add(field);
        await db.SaveChangesAsync(cancellationToken);
        return field;
    }

    public async Task<CatalogDeleteResult> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var field = await db.CustomFields.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (field is null)
        {
            return CatalogDeleteResult.Missing;
        }

        if (await db.DocumentCustomFieldValues.AnyAsync(item => item.CustomFieldId == id, cancellationToken))
        {
            return CatalogDeleteResult.Used;
        }

        db.CustomFields.Remove(field);
        await db.SaveChangesAsync(cancellationToken);
        return CatalogDeleteResult.Success;
    }
}

public sealed record CustomFieldOption(long Id, string Name, CustomFieldType Type);
