using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.Tags;

public sealed class TagStore(AppDbContext db, TimeProvider timeProvider)
{
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

        var existing = await db.Tags.Where(tag => normalizedNames.Contains(tag.Name)).ToDictionaryAsync(tag => tag.Name, cancellationToken);
        foreach (var name in normalizedNames)
        {
            if (!existing.TryGetValue(name, out var tag))
            {
                tag = new Tag { Name = name };
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
            document.Correspondent?.Name,
            document.DocumentType?.Name,
            document.ShelfFolder?.RelativePath
        }
        .Concat(document.Tags.Select(documentTag => documentTag.Tag.Name))
        .Concat(document.CustomFields.Select(value => $"{value.CustomField.Name} {value.Value}"))
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}
