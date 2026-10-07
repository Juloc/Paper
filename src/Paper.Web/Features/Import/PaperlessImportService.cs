using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Storage;
using Paper.Web.Features.Tags;

namespace Paper.Web.Features.Import;

public sealed class PaperlessImportService(
    AppDbContext db,
    IStorageProvider storage,
    TimeProvider timeProvider,
    ILogger<PaperlessImportService> logger)
{
    public async Task<PaperlessImportResult> ImportAsync(Stream source, long length, CancellationToken cancellationToken)
    {
        if (length <= 0 || length > DocumentRestoreLimit.MaximumBackupSize || !source.CanSeek)
        {
            return PaperlessImportResult.Failed("Der Paperless-Export ist leer, zu groß oder nicht lesbar.");
        }

        try
        {
            return await ImportArchiveAsync(source, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return PaperlessImportResult.Failed("Der Paperless-Export ist kein gültiges ZIP-Archiv.");
        }
        catch (JsonException)
        {
            return PaperlessImportResult.Failed("Das Paperless-Manifest enthält ungültiges JSON.");
        }
    }

    private async Task<PaperlessImportResult> ImportArchiveAsync(Stream source, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry is null)
        {
            return PaperlessImportResult.Failed("Der Paperless-Export enthält kein manifest.json.");
        }

        if (manifestEntry.Length > DocumentRestoreLimit.MaximumManifestSize)
        {
            return PaperlessImportResult.Failed("Das Paperless-Manifest ist zu groß.");
        }

        var catalogs = await ReadCatalogsAsync(manifestEntry, cancellationToken);
        if (catalogs is null)
        {
            return PaperlessImportResult.Failed("Das Paperless-Manifest enthält zu viele Einträge.");
        }

        if (!await ReadSplitCustomValuesAsync(archive, catalogs, cancellationToken))
        {
            return PaperlessImportResult.Failed("Die Split-Manifeste im Paperless-Export sind zu groß oder enthalten zu viele Einträge.");
        }
        var result = new PaperlessImportResult();
        var sizeBudget = new ImportSizeBudget(DocumentRestoreLimit.MaximumImportedBytes);
        await foreach (var fixture in ReadManifestAsync(manifestEntry, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(GetString(fixture, "model"), "documents.document", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await ImportDocumentAsync(archive, fixture, catalogs, result, sizeBudget, cancellationToken);
        }

        return result;
    }

    private async Task ImportDocumentAsync(
        ZipArchive archive,
        JsonElement fixture,
        PaperlessCatalogs catalogs,
        PaperlessImportResult result,
        ImportSizeBudget sizeBudget,
        CancellationToken cancellationToken)
    {
        var fields = fixture.TryGetProperty("fields", out var fieldElement) ? fieldElement : default;
        var originalName = GetString(fields, "original_filename") ?? GetString(fields, "filename") ?? $"paperless-{GetString(fixture, "pk")}.pdf";
        originalName = StoragePathPolicy.SanitizeFileName(Path.GetFileName(originalName.Replace('\\', '/')));
        var fileEntry = FindDocumentEntry(archive, fields);
        if (fileEntry is null)
        {
            result.Errors.Add($"{originalName}: Original- oder Archivdatei fehlt im Export.");
            return;
        }

        StoredDocument? stored = null;
        try
        {
            await using var source = fileEntry.Value.Entry.Open();
            await using var content = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (!sizeBudget.TryConsume(read))
                {
                    result.Errors.Add($"{originalName}: Der entpackte Import überschreitet das Limit von 2 GB.");
                    return;
                }

                if (content.Length + read > DocumentInputValidator.MaximumFileSize)
                {
                    result.Errors.Add($"{originalName}: Datei ist größer als 50 MB.");
                    return;
                }

                await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            content.Position = 0;
            var header = new byte[8];
            var headerLength = await DocumentInputValidator.ReadPrefixAsync(content, header, cancellationToken);
            if (!DocumentInputValidator.TryValidate(originalName, ContentTypeFor(originalName), content.Length, header.AsSpan(0, headerLength), out _, out var validationError))
            {
                result.Errors.Add($"{originalName}: {validationError}");
                return;
            }

            content.Position = 0;
            stored = await storage.SaveAsync(content, originalName, cancellationToken);
            if (await db.Documents.AnyAsync(document => document.Hash == stored.Hash, cancellationToken))
            {
                TryDeleteStored(stored, originalName);

                result.Skipped++;
                return;
            }

            var correspondent = await GetOrCreateCorrespondentAsync(catalogs.Correspondents, GetLong(fields, "correspondent"), cancellationToken);
            var documentType = await GetOrCreateDocumentTypeAsync(catalogs.DocumentTypes, GetLong(fields, "document_type"), cancellationToken);
            var documentId = GetLong(fixture, "pk");
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var ocrText = GetString(fields, "content");
            var document = new Document
            {
                Title = GetString(fields, "title")?.Trim() is { Length: > 0 } title ? title[..Math.Min(300, title.Length)] : Path.GetFileNameWithoutExtension(originalName),
                DocumentDate = ParseDate(GetString(fields, "document_date")),
                OriginalFileName = originalName,
                FilePath = stored.RelativePath,
                FileSize = stored.Size,
                Hash = stored.Hash,
                OcrText = ocrText,
                OcrStatus = string.IsNullOrWhiteSpace(ocrText) ? OcrStatus.Pending : OcrStatus.Completed,
                Status = DocumentStatus.Inbox,
                Correspondent = correspondent,
                DocumentType = documentType,
                CreatedAt = ParseUtc(GetString(fields, "created"), now),
                UpdatedAt = ParseUtc(GetString(fields, "modified"), now),
                SearchText = ""
            };

            var importedTagNames = GetLongArray(fields, "tags")
                .Where(catalogs.Tags.ContainsKey)
                .Select(tagId => catalogs.Tags[tagId].Trim().ToLowerInvariant())
                .Where(name => name.Length is > 0 and <= 80)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (var name in importedTagNames)
            {
                var tag = await db.Tags.SingleOrDefaultAsync(item => item.Name == name, cancellationToken);
                if (tag is null)
                {
                    tag = new Tag { Name = name };
                    db.Tags.Add(tag);
                }

                document.Tags.Add(new DocumentTag { Document = document, Tag = tag });
            }

            foreach (var value in ReadCustomValues(fields, documentId, catalogs)
                         .GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First()))
            {
                if (!CustomFieldValuePolicy.IsValid(value.Type, value.Value.Trim()))
                {
                    continue;
                }

                var comparisonFieldName = value.Name.ToLowerInvariant();
                var customField = await db.CustomFields.SingleOrDefaultAsync(item => item.NameKey == comparisonFieldName, cancellationToken);
                if (customField is null)
                {
                    customField = new CustomField { Name = value.Name[..Math.Min(120, value.Name.Length)], Type = value.Type };
                    db.CustomFields.Add(customField);
                }

                document.CustomFields.Add(new DocumentCustomFieldValue
                {
                    Document = document,
                    CustomField = customField,
                    Value = value.Value.Trim()[..Math.Min(2000, value.Value.Trim().Length)]
                });
            }

            document.SearchText = TagStore.BuildSearchText(document);
            db.Documents.Add(document);
            if (document.OcrStatus == OcrStatus.Pending)
            {
                db.ProcessingJobs.Add(new ProcessingJob
                {
                    Document = document,
                    Type = ProcessingJobType.OcrAndAnalyze,
                    State = ProcessingJobState.Pending,
                    Priority = 10,
                    Attempts = 0,
                    CreatedAt = now
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            result.Imported++;
        }
        catch (DbUpdateException exception) when (DocumentPersistenceErrors.IsDuplicateHash(exception))
        {
            db.ChangeTracker.Clear();
            TryDeleteStored(stored, originalName);

            logger.LogInformation("Skipped concurrently imported Paperless duplicate document {FileName}.", originalName);
            result.Skipped++;
        }
        catch (OperationCanceledException)
        {
            TryDeleteStored(stored, originalName);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            TryDeleteStored(stored, originalName);

            logger.LogError(exception, "Could not import Paperless document {FileName}.", originalName);
            result.Errors.Add($"{originalName}: Import fehlgeschlagen.");
        }
    }

    private void TryDeleteStored(StoredDocument? stored, string fileName)
    {
        if (stored is null || stored.AlreadyExisted)
        {
            return;
        }

        try
        {
            storage.Delete(stored.RelativePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not clean up Paperless file {FileName} at {RelativePath}.", fileName, stored.RelativePath);
        }
    }

    private static (ZipArchiveEntry Entry, string Name)? FindDocumentEntry(ZipArchive archive, JsonElement fields)
    {
        var candidates = new[]
        {
            GetString(fields, "filename"),
            GetString(fields, "original_filename"),
            GetString(fields, "archive_filename")
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .SelectMany(value => new[]
        {
            value!,
            $"originals/{value}",
            $"archive/{value}",
            $"documents/{value}"
        })
        .Select(NormalizeZipPath)
        .Where(value => value is not null)
        .Select(value => value!)
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var entry = archive.GetEntry(candidate);
            if (entry is not null && !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                return (entry, candidate);
            }
        }

        return null;
    }

    private static async Task<PaperlessCatalogs?> ReadCatalogsAsync(
        ZipArchiveEntry manifestEntry,
        CancellationToken cancellationToken)
    {
        var catalogs = new PaperlessCatalogs();
        var count = 0;
        await foreach (var fixture in ReadManifestAsync(manifestEntry, cancellationToken))
        {
            if (++count > DocumentRestoreLimit.MaximumManifestEntries)
            {
                return null;
            }

            AddCatalogEntry(catalogs, fixture);
        }

        return catalogs;
    }

    private static async IAsyncEnumerable<JsonElement> ReadManifestAsync(
        ZipArchiveEntry manifestEntry,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = manifestEntry.Open();
        await foreach (var fixture in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, cancellationToken: cancellationToken))
        {
            yield return fixture;
        }
    }

    private static void AddCatalogEntry(PaperlessCatalogs catalogs, JsonElement fixture)
    {
        var model = GetString(fixture, "model")?.ToLowerInvariant();
        var id = GetLong(fixture, "pk");
        if (id is null || !fixture.TryGetProperty("fields", out var fields))
        {
            return;
        }

        if (model is "documents.customfieldinstance")
        {
            var documentId = GetLong(fields, "document");
            var fieldId = GetLong(fields, "field");
            var value = GetString(fields, "value");
            if (documentId is not null && fieldId is not null && value is not null)
            {
                catalogs.GetOrAdd(documentId.Value).Add(new PaperlessCustomValue(fieldId.Value, value));
            }

            return;
        }

        var name = GetString(fields, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (model is "documents.tag")
        {
            catalogs.Tags[id.Value] = name;
        }
        else if (model is "documents.correspondent")
        {
            catalogs.Correspondents[id.Value] = name;
        }
        else if (model is "documents.doctype" or "documents.documenttype")
        {
            catalogs.DocumentTypes[id.Value] = name;
        }
        else if (model is "documents.customfield")
        {
            catalogs.CustomFields[id.Value] = new PaperlessCustomField(name, MapType(GetString(fields, "data_type")));
        }
    }

    private static IEnumerable<PaperlessValue> ReadCustomValues(JsonElement fields, long? documentId, PaperlessCatalogs catalogs)
    {
        if (fields.TryGetProperty("custom_fields", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in values.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var fieldId = GetLong(item, "field") ?? GetLong(item, "id");
                var value = GetString(item, "value");
                if (fieldId is not null && value is not null && catalogs.CustomFields.TryGetValue(fieldId.Value, out var definition))
                {
                    yield return new PaperlessValue(definition.Name, definition.Type, value);
                }
            }
        }
        else if (fields.TryGetProperty("custom_fields", out values) && values.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in values.EnumerateObject())
            {
                if (!long.TryParse(property.Name, out var fieldId) || !catalogs.CustomFields.TryGetValue(fieldId, out var definition))
                {
                    continue;
                }

                var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return new PaperlessValue(definition.Name, definition.Type, value);
                }
            }
        }

        if (documentId is not null && catalogs.CustomValuesByDocument.TryGetValue(documentId.Value, out var instances))
        {
            foreach (var instance in instances)
            {
                if (catalogs.CustomFields.TryGetValue(instance.FieldId, out var definition))
                {
                    yield return new PaperlessValue(definition.Name, definition.Type, instance.Value);
                }
            }
        }
    }

    private static async Task<bool> ReadSplitCustomValuesAsync(
        ZipArchive archive,
        PaperlessCatalogs catalogs,
        CancellationToken cancellationToken)
    {
        foreach (var entry in archive.Entries.Where(entry =>
                     entry.FullName.EndsWith("-manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            if (entry.Length > DocumentRestoreLimit.MaximumManifestSize)
            {
                return false;
            }

            await using var stream = entry.Open();
            var count = 0;
            await foreach (var fixture in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, cancellationToken: cancellationToken))
            {
                if (++count > DocumentRestoreLimit.MaximumManifestEntries)
                {
                    return false;
                }

                if (!string.Equals(GetString(fixture, "model"), "documents.customfieldinstance", StringComparison.OrdinalIgnoreCase) ||
                    !fixture.TryGetProperty("fields", out var fields))
                {
                    continue;
                }

                var documentId = GetLong(fields, "document");
                var fieldId = GetLong(fields, "field");
                var value = GetString(fields, "value");
                if (documentId is not null && fieldId is not null && value is not null)
                {
                    catalogs.GetOrAdd(documentId.Value).Add(new PaperlessCustomValue(fieldId.Value, value));
                }
            }
        }

        return true;
    }

    private async Task<Correspondent?> GetOrCreateCorrespondentAsync(IReadOnlyDictionary<long, string> definitions, long? id, CancellationToken cancellationToken)
    {
        if (id is null || !definitions.TryGetValue(id.Value, out var name))
        {
            return null;
        }

        var comparisonName = name.ToLowerInvariant();
        var existing = await db.Correspondents.SingleOrDefaultAsync(item => item.NameKey == comparisonName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var correspondent = new Correspondent { Name = name[..Math.Min(200, name.Length)] };
        db.Correspondents.Add(correspondent);
        return correspondent;
    }

    private async Task<DocumentType?> GetOrCreateDocumentTypeAsync(IReadOnlyDictionary<long, string> definitions, long? id, CancellationToken cancellationToken)
    {
        if (id is null || !definitions.TryGetValue(id.Value, out var name))
        {
            return null;
        }

        var comparisonName = name.ToLowerInvariant();
        var existing = await db.DocumentTypes.SingleOrDefaultAsync(item => item.NameKey == comparisonName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var documentType = new DocumentType { Name = name[..Math.Min(120, name.Length)] };
        db.DocumentTypes.Add(documentType);
        return documentType;
    }

    private static string? NormalizeZipPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 || segments.Any(segment => segment is "." or "..") ? null : string.Join('/', segments);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;

    private static long? GetLong(JsonElement element, string name)
    {
        var value = GetString(element, name);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static IEnumerable<long> GetLongArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var id))
            {
                yield return id;
            }
        }
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static DateTime ParseUtc(string? value, DateTime fallback) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date
            : fallback;

    private static CustomFieldType MapType(string? value) => value?.ToLowerInvariant() switch
    {
        "date" => CustomFieldType.Date,
        "integer" or "float" or "monetary" => CustomFieldType.Number,
        "boolean" => CustomFieldType.Boolean,
        _ => CustomFieldType.Text
    };

    private static string? ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".tif" or ".tiff" => "image/tiff",
        _ => null
    };

    private sealed class PaperlessCatalogs
    {
        public Dictionary<long, string> Tags { get; } = [];
        public Dictionary<long, string> Correspondents { get; } = [];
        public Dictionary<long, string> DocumentTypes { get; } = [];
        public Dictionary<long, PaperlessCustomField> CustomFields { get; } = [];
        public Dictionary<long, List<PaperlessCustomValue>> CustomValuesByDocument { get; } = [];

        public List<PaperlessCustomValue> GetOrAdd(long documentId)
        {
            if (!CustomValuesByDocument.TryGetValue(documentId, out var values))
            {
                values = [];
                CustomValuesByDocument[documentId] = values;
            }

            return values;
        }
    }

    private sealed record PaperlessCustomField(string Name, CustomFieldType Type);
    private sealed record PaperlessCustomValue(long FieldId, string Value);
    private sealed record PaperlessValue(string Name, CustomFieldType Type, string Value);
}

public sealed class PaperlessImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; } = [];

    public static PaperlessImportResult Failed(string error) => new() { Errors = { error } };
}

internal static class DocumentRestoreLimit
{
    public const long MaximumBackupSize = 2L * 1024 * 1024 * 1024;
    public const long MaximumImportedBytes = 2L * 1024 * 1024 * 1024;
    public const long MaximumManifestSize = 256L * 1024 * 1024;
    public const int MaximumManifestEntries = 100_000;
}
