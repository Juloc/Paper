using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.Tags;

public sealed class TagStore(AppDbContext db, TimeProvider timeProvider)
{
    public Task<List<TagOption>> ListAsync(CancellationToken cancellationToken) =>
        db.Tags.AsNoTracking()
            .OrderBy(tag => tag.Name)
            .Select(tag => new TagOption(tag.Id, tag.Name, tag.Documents.Count))
            .ToListAsync(cancellationToken);

    public async Task<Tag?> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim().ToLowerInvariant();
        if (normalizedName.Length is 0 or > 80)
        {
            return null;
        }

        var existing = await db.Tags.SingleOrDefaultAsync(tag => tag.NameKey == normalizedName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var tag = new Tag { Name = normalizedName, NameKey = normalizedName };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);
        return tag;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var tag = await db.Tags.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (tag is null)
        {
            return false;
        }

        var documents = await db.Documents
            .AsSplitQuery()
            .Include(document => document.Tags).ThenInclude(documentTag => documentTag.Tag)
            .Include(document => document.CustomFields).ThenInclude(value => value.CustomField)
            .Include(document => document.Correspondent)
            .Include(document => document.DocumentType)
            .Include(document => document.ShelfFolder)
            .Where(document => document.Tags.Any(documentTag => documentTag.TagId == id))
            .ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            foreach (var link in document.Tags.Where(documentTag => documentTag.TagId == id).ToArray())
            {
                db.DocumentTags.Remove(link);
                document.Tags.Remove(link);
            }

            document.SearchText = BuildSearchText(document);
            document.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        }

        db.Tags.Remove(tag);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task AddNamesAsync(Document document, IEnumerable<string> names, CancellationToken cancellationToken)
    {
        var normalizedNames = names
            .Select(name => name.Trim().ToLowerInvariant())
            .Where(name => name.Length is > 0 and <= 80)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedNames.Length == 0)
        {
            return;
        }

        var existing = await db.Tags.Where(tag => normalizedNames.Contains(tag.NameKey)).ToDictionaryAsync(tag => tag.NameKey, cancellationToken);
        foreach (var name in normalizedNames)
        {
            if (!existing.TryGetValue(name, out var tag))
            {
                tag = new Tag { Name = name, NameKey = name };
                db.Tags.Add(tag);
            }

            if (document.Tags.All(documentTag => documentTag.Tag.Name != name))
            {
                document.Tags.Add(new DocumentTag { Document = document, Tag = tag });
            }
        }

        document.SearchText = BuildSearchText(document);
        document.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
    }

    public static string BuildSearchText(Document document) => string.Join(
        ' ',
        new[]
        {
            document.Title,
            document.OriginalFileName,
            document.OcrText,
            document.FilePath,
            document.Correspondent?.Name,
            document.DocumentType?.Name,
            document.ShelfFolder?.RelativePath
        }
        .Concat(document.Tags.Select(documentTag => documentTag.Tag.Name))
        .Concat(document.CustomFields.Select(value => $"{value.CustomField.Name} {value.Value}"))
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed record TagOption(long Id, string Name, int DocumentCount);
