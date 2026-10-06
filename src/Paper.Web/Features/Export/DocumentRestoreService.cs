using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Storage;

namespace Paper.Web.Features.Export;

public sealed class DocumentRestoreService(
    AppDbContext db,
    IStorageProvider storage,
    DocumentFilingService filing,
    TimeProvider timeProvider,
    ILogger<DocumentRestoreService> logger)
{
    public const long MaximumBackupSize = 2L * 1024 * 1024 * 1024;
    private const long MaximumManifestSize = 256L * 1024 * 1024;

    public async Task<RestoreResult> RestoreAsync(Stream source, long length, CancellationToken cancellationToken)
    {
        if (length <= 0 || length > MaximumBackupSize || !source.CanSeek)
        {
            return RestoreResult.Failed("Das Backup ist leer, zu groß oder nicht lesbar.");
        }

        try
        {
            return await RestoreArchiveAsync(source, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return RestoreResult.Failed("Das Backup ist kein gültiges ZIP-Archiv.");
        }
        catch (JsonException)
        {
            return RestoreResult.Failed("Das Backup-Manifest enthält ungültiges JSON.");
        }
    }

    private async Task<RestoreResult> RestoreArchiveAsync(Stream source, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry is null)
        {
            return RestoreResult.Failed("Das Backup enthält kein manifest.json.");
        }

        if (manifestEntry.Length > MaximumManifestSize)
        {
            return RestoreResult.Failed("Das Backup-Manifest ist zu groß.");
        }

        if (!await IsValidManifestAsync(manifestEntry, cancellationToken))
        {
            return RestoreResult.Failed("Das Backup-Manifest ist ungültig.");
        }

        var result = new RestoreResult();
        await using (var manifestStream = manifestEntry.Open())
        {
            var count = 0;
            await foreach (var entry in JsonSerializer.DeserializeAsyncEnumerable<DocumentBackupManifestEntry>(manifestStream, cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry is null || ++count > 100_000)
                {
                    return RestoreResult.Failed("Das Backup enthält zu viele oder ungültige Dokumente.");
                }

                await RestoreDocumentAsync(archive, entry, result, cancellationToken);
            }
        }

        await RestoreAnalysisRulesAsync(archive, result, cancellationToken);

        return result;
    }

    private static async Task<bool> IsValidManifestAsync(ZipArchiveEntry manifestEntry, CancellationToken cancellationToken)
    {
        await using var stream = manifestEntry.Open();
        var count = 0;
        await foreach (var entry in JsonSerializer.DeserializeAsyncEnumerable<DocumentBackupManifestEntry>(stream, cancellationToken: cancellationToken))
        {
            if (entry is null || ++count > 100_000)
            {
                return false;
            }
        }

        return true;
    }

    private async Task RestoreAnalysisRulesAsync(ZipArchive archive, RestoreResult result, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("analysis-rules.json");
        if (entry is null)
        {
            return;
        }

        List<AnalysisRuleBackupEntry>? rules;
        await using (var stream = entry.Open())
        {
            rules = await JsonSerializer.DeserializeAsync<List<AnalysisRuleBackupEntry>>(stream, cancellationToken: cancellationToken);
        }

        if (rules is null || rules.Count > 100_000)
        {
            result.Errors.Add("Die Analyse-Regeln im Backup sind ungültig oder zu zahlreich.");
            return;
        }

        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var term = rule.Term?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(term) || term.Length > 80 || rule.UseCount < 1 ||
                (string.IsNullOrWhiteSpace(rule.Correspondent) && string.IsNullOrWhiteSpace(rule.DocumentType) && string.IsNullOrWhiteSpace(rule.ShelfPath)))
            {
                continue;
            }

            var correspondent = await FindOrCreateCorrespondentAsync(rule.Correspondent, cancellationToken);
            var documentType = await FindOrCreateDocumentTypeAsync(rule.DocumentType, cancellationToken);
            var shelf = await FindOrCreateShelfAsync(rule.ShelfPath, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            var correspondentId = correspondent?.Id;
            var documentTypeId = documentType?.Id;
            var shelfId = shelf?.Id;
            var existing = await db.AnalysisRules.SingleOrDefaultAsync(item =>
                item.Term == term &&
                item.CorrespondentId == correspondentId &&
                item.DocumentTypeId == documentTypeId &&
                item.ShelfFolderId == shelfId, cancellationToken);
            if (existing is not null)
            {
                existing.UseCount = Math.Max(existing.UseCount, rule.UseCount);
                existing.UpdatedAt = rule.UpdatedAt == default ? timeProvider.GetUtcNow().UtcDateTime : rule.UpdatedAt;
                continue;
            }

            db.AnalysisRules.Add(new AnalysisRule
            {
                Term = term,
                CorrespondentId = correspondent?.Id,
                DocumentTypeId = documentType?.Id,
                ShelfFolderId = shelf?.Id,
                UseCount = rule.UseCount,
                CreatedAt = rule.CreatedAt == default ? timeProvider.GetUtcNow().UtcDateTime : rule.CreatedAt,
                UpdatedAt = rule.UpdatedAt == default ? timeProvider.GetUtcNow().UtcDateTime : rule.UpdatedAt
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RestoreDocumentAsync(
        ZipArchive archive,
        DocumentBackupManifestEntry entry,
        RestoreResult result,
        CancellationToken cancellationToken)
    {
        var fileName = StoragePathPolicy.SanitizeFileName(Path.GetFileName(entry.OriginalFileName.Replace('\\', '/')));
        var backupPath = $"documents/{entry.Id:D8}_{StoragePathPolicy.SanitizeFileName(fileName)}";
        var fileEntry = archive.GetEntry(backupPath);
        if (fileEntry is null)
        {
            result.Errors.Add($"{fileName}: Datei fehlt im Backup.");
            return;
        }

        StoredDocument? stored = null;
        try
        {
            await using var source = fileEntry.Open();
            await using var content = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (content.Length + read > DocumentInputValidator.MaximumFileSize)
                {
                    result.Errors.Add($"{fileName}: Datei ist größer als 50 MB.");
                    return;
                }

                await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            content.Position = 0;
            var header = new byte[8];
            var headerLength = await content.ReadAsync(header, cancellationToken);
            if (!DocumentInputValidator.TryValidate(
                    fileName,
                    ContentTypeFor(fileName),
                    content.Length,
                    header.AsSpan(0, headerLength),
                    out _,
                    out var validationError))
            {
                result.Errors.Add($"{fileName}: {validationError}");
                return;
            }

            content.Position = 0;
            stored = await storage.SaveAsync(content, fileName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(entry.Hash) &&
                !string.Equals(entry.Hash, stored.Hash, StringComparison.OrdinalIgnoreCase))
            {
                if (!stored.AlreadyExisted)
                {
                    storage.Delete(stored.RelativePath);
                }
                result.Errors.Add($"{fileName}: Hash stimmt nicht mit dem Manifest überein.");
                return;
            }

            if (await db.Documents.AnyAsync(document => document.Hash == stored.Hash, cancellationToken))
            {
                if (!stored.AlreadyExisted)
                {
                    storage.Delete(stored.RelativePath);
                }
                result.Skipped++;
                return;
            }

            var correspondent = await FindOrCreateCorrespondentAsync(entry.Correspondent, cancellationToken);
            var documentType = await FindOrCreateDocumentTypeAsync(entry.DocumentType, cancellationToken);
            var shelf = await FindOrCreateShelfAsync(entry.ShelfPath, cancellationToken);
            var customFieldDefinitions = await FindOrCreateCustomFieldsAsync(entry.CustomFields ?? [], cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var createdAt = entry.CreatedAt == default ? now : entry.CreatedAt;
            var updatedAt = entry.UpdatedAt == default ? now : entry.UpdatedAt;
            var restoredStatus = Enum.TryParse<DocumentStatus>(entry.Status, ignoreCase: true, out var parsedStatus)
                ? parsedStatus
                : DocumentStatus.Inbox;
            if (restoredStatus == DocumentStatus.Filed && shelf is null)
            {
                if (!stored.AlreadyExisted)
                {
                    storage.Delete(stored.RelativePath);
                }

                result.Errors.Add($"{fileName}: Ein abgelegtes Dokument benötigt einen Regalpfad.");
                return;
            }

            var ocrStatus = Enum.TryParse<OcrStatus>(entry.OcrStatus, ignoreCase: true, out var parsedOcrStatus)
                ? parsedOcrStatus
                : OcrStatus.Pending;
            var restoredTitle = string.IsNullOrWhiteSpace(entry.Title)
                ? Path.GetFileNameWithoutExtension(fileName).Trim()
                : entry.Title.Trim();
            restoredTitle = string.IsNullOrWhiteSpace(restoredTitle) ? "Dokument" : restoredTitle[..Math.Min(300, restoredTitle.Length)];
            var document = new Document
            {
                Title = restoredTitle,
                DocumentDate = entry.DocumentDate,
                OriginalFileName = fileName,
                FilePath = stored.RelativePath,
                FileSize = stored.Size,
                Hash = stored.Hash,
                OcrText = entry.OcrText,
                OcrStatus = ocrStatus,
                OcrError = entry.OcrError,
                Status = restoredStatus == DocumentStatus.Filed ? DocumentStatus.Inbox : restoredStatus,
                Correspondent = correspondent,
                DocumentType = documentType,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                SearchText = string.Join(' ', entry.Title, fileName, entry.OcrText ?? "")
            };
            db.Documents.Add(document);
            if (ocrStatus is OcrStatus.Pending or OcrStatus.Processing)
            {
                document.OcrStatus = OcrStatus.Pending;
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
            var customValues = customFieldDefinitions
                .Select(field => (field.Id, Value: (entry.CustomFields ?? []).FirstOrDefault(value => value.Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase))?.Value))
                .Where(value => value.Value is not null)
                .ToDictionary(value => value.Id, value => value.Value!);
            var saved = await filing.SaveAsync(
                document.Id,
                new DocumentEdit(
                    document.Title,
                    document.DocumentDate,
                    correspondent?.Id,
                    documentType?.Id,
                    shelf?.Id,
                    string.Join(", ", entry.Tags ?? []),
                    customValues),
                fileFromInbox: restoredStatus == DocumentStatus.Filed,
                cancellationToken);
            if (!saved.Succeeded)
            {
                db.Documents.Remove(document);
                await db.SaveChangesAsync(cancellationToken);
                result.Errors.Add($"{fileName}: Metadaten konnten nicht wiederhergestellt werden: {saved.Error}");
                return;
            }

            result.Imported++;
        }
        catch (DbUpdateException exception) when (DocumentPersistenceErrors.IsDuplicateHash(exception))
        {
            db.ChangeTracker.Clear();
            if (stored is not null && !stored.AlreadyExisted)
            {
                storage.Delete(stored.RelativePath);
            }

            logger.LogInformation("Skipped concurrently restored duplicate document {FileName}.", fileName);
            result.Skipped++;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            if (stored is not null && !stored.AlreadyExisted)
            {
                storage.Delete(stored.RelativePath);
            }

            logger.LogError(exception, "Could not restore backup document {FileName}.", fileName);
            result.Errors.Add($"{fileName}: Wiederherstellung fehlgeschlagen.");
        }
    }

    private async Task<Correspondent?> FindOrCreateCorrespondentAsync(string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim();
        var existing = await db.Correspondents.SingleOrDefaultAsync(item => item.Name == normalized, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var correspondent = new Correspondent { Name = normalized[..Math.Min(200, normalized.Length)] };
        db.Correspondents.Add(correspondent);
        return correspondent;
    }

    private async Task<DocumentType?> FindOrCreateDocumentTypeAsync(string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim();
        var existing = await db.DocumentTypes.SingleOrDefaultAsync(item => item.Name == normalized, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var documentType = new DocumentType { Name = normalized[..Math.Min(120, normalized.Length)] };
        db.DocumentTypes.Add(documentType);
        return documentType;
    }

    private async Task<ShelfFolder?> FindOrCreateShelfAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalizedPath = StoragePathPolicy.NormalizeFolderPath(path);
        ShelfFolder? parent = null;
        foreach (var segment in normalizedPath.Split('/'))
        {
            var currentPath = parent is null ? segment : $"{parent.RelativePath}/{segment}";
            var folder = await db.ShelfFolders.SingleOrDefaultAsync(item => item.RelativePath == currentPath, cancellationToken);
            if (folder is null)
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                folder = new ShelfFolder
                {
                    Parent = parent,
                    Name = segment,
                    RelativePath = currentPath,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.ShelfFolders.Add(folder);
            }

            parent = folder;
        }

        return parent;
    }

    private async Task<List<CustomField>> FindOrCreateCustomFieldsAsync(
        IReadOnlyCollection<DocumentBackupCustomField> values,
        CancellationToken cancellationToken)
    {
        var fields = new List<CustomField>();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value.Name)))
        {
            var name = value.Name.Trim();
            var field = await db.CustomFields.SingleOrDefaultAsync(item => item.Name == name, cancellationToken);
            if (field is null)
            {
                var type = Enum.TryParse<CustomFieldType>(value.Type, ignoreCase: true, out var parsed) ? parsed : CustomFieldType.Text;
                field = new CustomField { Name = name[..Math.Min(120, name.Length)], Type = type };
                db.CustomFields.Add(field);
            }

            if (fields.All(existing => existing.Name != field.Name))
            {
                fields.Add(field);
            }
        }

        return fields;
    }

    private static string? ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".tif" or ".tiff" => "image/tiff",
        _ => null
    };
}

public sealed class RestoreResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; } = [];

    public static RestoreResult Failed(string error) => new() { Errors = { error } };
}
