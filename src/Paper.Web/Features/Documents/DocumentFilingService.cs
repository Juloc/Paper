using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.Storage;

namespace Paper.Web.Features.Documents;

public sealed class DocumentFilingService(
    AppDbContext db,
    IStorageProvider storage,
    TimeProvider timeProvider,
    ILogger<DocumentFilingService> logger)
{
    public async Task<DocumentSaveResult> SaveAsync(
        long id,
        DocumentEdit edit,
        bool fileFromInbox,
        CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsSplitQuery()
            .Include(item => item.Tags).ThenInclude(item => item.Tag)
            .Include(item => item.CustomFields).ThenInclude(item => item.CustomField)
            .Include(item => item.ShelfFolder)
            .Include(item => item.Correspondent)
            .Include(item => item.DocumentType)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null)
        {
            return DocumentSaveResult.Missing;
        }

        var title = edit.Title.Trim();
        if (title.Length is 0 or > 300)
        {
            return DocumentSaveResult.Invalid("Bitte einen gültigen Titel eingeben.");
        }

        ShelfFolder? shelf = null;
        if (edit.ShelfFolderId is not null)
        {
            shelf = await db.ShelfFolders.SingleOrDefaultAsync(item => item.Id == edit.ShelfFolderId, cancellationToken);
            if (shelf is null)
            {
                return DocumentSaveResult.Invalid("Der ausgewählte Regalordner existiert nicht.");
            }
        }

        if ((fileFromInbox || document.Status == DocumentStatus.Filed) && shelf is null)
        {
            return DocumentSaveResult.Invalid("Bitte einen Regalordner auswählen.");
        }

        Correspondent? correspondent = null;
        if (edit.CorrespondentId is not null)
        {
            correspondent = await db.Correspondents.SingleOrDefaultAsync(item => item.Id == edit.CorrespondentId, cancellationToken);
            if (correspondent is null)
            {
                return DocumentSaveResult.Invalid("Der ausgewählte Korrespondent existiert nicht.");
            }
        }

        DocumentType? documentType = null;
        if (edit.DocumentTypeId is not null)
        {
            documentType = await db.DocumentTypes.SingleOrDefaultAsync(item => item.Id == edit.DocumentTypeId, cancellationToken);
            if (documentType is null)
            {
                return DocumentSaveResult.Invalid("Der ausgewählte Dokumenttyp existiert nicht.");
            }
        }

        var customFields = await db.CustomFields.AsNoTracking().ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var fieldValue in edit.CustomFields)
        {
            if (!customFields.TryGetValue(fieldValue.Key, out var field) || string.IsNullOrWhiteSpace(fieldValue.Value))
            {
                continue;
            }

            if (!IsValidValue(field.Type, fieldValue.Value.Trim()))
            {
                return DocumentSaveResult.Invalid($"Der Wert für „{field.Name}“ hat das falsche Format.");
            }
        }

        var oldPath = document.FilePath;
        string? newPath = null;
        var movedFile = false;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (fileFromInbox || document.Status == DocumentStatus.Filed)
            {
                newPath = await storage.MoveToShelfAsync(
                    document.FilePath,
                    shelf!.RelativePath,
                    edit.DocumentDate,
                    title,
                    document.OriginalFileName,
                    cancellationToken);
                document.FilePath = newPath;
                movedFile = !string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase);
            }

            document.Title = title;
            document.DocumentDate = edit.DocumentDate;
            document.CorrespondentId = correspondent?.Id;
            document.Correspondent = correspondent;
            document.DocumentTypeId = documentType?.Id;
            document.DocumentType = documentType;
            document.ShelfFolderId = shelf?.Id ?? document.ShelfFolderId;
            document.ShelfFolder = shelf ?? document.ShelfFolder;
            document.Status = fileFromInbox ? DocumentStatus.Filed : document.Status;
            ReplaceTags(document, edit.Tags);
            ReplaceCustomFields(document, edit.CustomFields, customFields);
            document.SearchText = Features.Tags.TagStore.BuildSearchText(document);
            document.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return DocumentSaveResult.Success;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (movedFile && newPath is not null)
            {
                try
                {
                    await storage.MoveBackAsync(newPath, oldPath, CancellationToken.None);
                }
                catch (Exception rollbackError)
                {
                    logger.LogError(rollbackError, "Could not roll back document move from {NewPath} to {OldPath}.", newPath, oldPath);
                }
            }

            throw;
        }
    }

    private void ReplaceTags(Document document, string? tags)
    {
        var names = (tags ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.ToLowerInvariant())
            .Where(name => name.Length is > 0 and <= 80)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var existing = document.Tags.ToDictionary(item => item.Tag.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var link in document.Tags.Where(link => !names.Contains(link.Tag.Name, StringComparer.OrdinalIgnoreCase)).ToArray())
        {
            db.DocumentTags.Remove(link);
            document.Tags.Remove(link);
        }

        foreach (var name in names)
        {
            if (existing.ContainsKey(name))
            {
                continue;
            }

            var tag = db.Tags.Local.FirstOrDefault(item => item.Name == name);
            if (tag is null)
            {
                tag = db.Tags.Local.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
            if (tag is null)
            {
                tag = db.Tags.FirstOrDefault(item => item.Name == name);
            }
            tag ??= new Tag { Name = name };
            if (tag.Id == 0 && db.Entry(tag).State == EntityState.Detached)
            {
                db.Tags.Add(tag);
            }

            document.Tags.Add(new DocumentTag { Document = document, Tag = tag });
        }
    }

    private void ReplaceCustomFields(
        Document document,
        IReadOnlyDictionary<long, string> values,
        IReadOnlyDictionary<long, CustomField> definitions)
    {
        foreach (var existing in document.CustomFields.ToArray())
        {
            db.DocumentCustomFieldValues.Remove(existing);
            document.CustomFields.Remove(existing);
        }

        document.CustomFields.Clear();
        foreach (var value in values.Where(item => !string.IsNullOrWhiteSpace(item.Value)))
        {
            if (!definitions.ContainsKey(value.Key))
            {
                continue;
            }

            document.CustomFields.Add(new DocumentCustomFieldValue
            {
                Document = document,
                CustomFieldId = value.Key,
                CustomField = definitions[value.Key],
                Value = value.Value.Trim()
            });
        }
    }

    private static bool IsValidValue(CustomFieldType type, string value) => type switch
    {
        CustomFieldType.Text => value.Length <= 2000,
        CustomFieldType.Number => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
        CustomFieldType.Date => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        CustomFieldType.Boolean => bool.TryParse(value, out _),
        _ => false
    };
}

public sealed record DocumentEdit(
    string Title,
    DateOnly? DocumentDate,
    long? CorrespondentId,
    long? DocumentTypeId,
    long? ShelfFolderId,
    string? Tags,
    IReadOnlyDictionary<long, string> CustomFields);

public sealed record DocumentSaveResult(bool Succeeded, bool NotFound, string? Error)
{
    public static DocumentSaveResult Success { get; } = new(true, false, null);
    public static DocumentSaveResult Missing { get; } = new(false, true, null);
    public static DocumentSaveResult Invalid(string error) => new(false, false, error);
}
