using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

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
        if (normalizedName.Length is 0 or > 120)
        {
            return null;
        }

        var existing = await db.CustomFields.SingleOrDefaultAsync(item => item.Name == normalizedName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var field = new CustomField { Name = normalizedName, Type = type };
        db.CustomFields.Add(field);
        await db.SaveChangesAsync(cancellationToken);
        return field;
    }
}

public sealed record CustomFieldOption(long Id, string Name, CustomFieldType Type);
