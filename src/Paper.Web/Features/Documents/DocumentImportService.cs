using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Storage;

namespace Paper.Web.Features.Documents;

public sealed class DocumentImportService(
    AppDbContext db,
    IStorageProvider storage,
    TimeProvider timeProvider,
    ILogger<DocumentImportService> logger)
{
    public async Task<ImportResult> ImportAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var input = file.OpenReadStream();
        return await ImportAsync(input, file.FileName, file.ContentType, file.Length, cancellationToken);
    }

    public async Task<ImportResult> ImportAsync(
        Stream input,
        string fileName,
        string? contentType,
        long length,
        CancellationToken cancellationToken)
    {
        fileName = StoragePathPolicy.SanitizeFileName(Path.GetFileName(fileName.Replace('\\', '/')));
        if (length > DocumentInputValidator.MaximumFileSize || length <= 0)
        {
            return ImportResult.Failed("Die Datei ist leer oder größer als 50 MB.");
        }

        var header = new byte[8];
        var headerLength = await DocumentInputValidator.ReadPrefixAsync(input, header, cancellationToken);
        if (!DocumentInputValidator.TryValidate(fileName, contentType, length, header.AsSpan(0, headerLength), out _, out var error))
        {
            return ImportResult.Failed(error);
        }

        if (!input.CanSeek)
        {
            return ImportResult.Failed("Die Datei konnte nicht geprüft werden.");
        }

        input.Position = 0;
        StoredDocument stored;
        try
        {
            stored = await storage.SaveAsync(input, fileName, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not store imported document {FileName}.", fileName);
            return ImportResult.Failed("Das Dokument konnte nicht im Dokumentenspeicher abgelegt werden. Bitte prüfe die Speicherverbindung.");
        }

        var duplicate = await db.Documents.AsNoTracking().AnyAsync(document => document.Hash == stored.Hash, cancellationToken);
        if (duplicate)
        {
            if (!stored.AlreadyExisted)
            {
                TryDeleteStored(stored.RelativePath, fileName);
            }
            return ImportResult.Failed("Dieses Dokument ist bereits vorhanden.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var title = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (title.Length == 0)
        {
            title = "Dokument";
        }

        var document = new Document
        {
            Title = title,
            OriginalFileName = Path.GetFileName(fileName),
            FilePath = stored.RelativePath,
            FileSize = stored.Size,
            Hash = stored.Hash,
            OcrStatus = OcrStatus.Pending,
            Status = DocumentStatus.Inbox,
            CreatedAt = now,
            UpdatedAt = now,
            SearchText = string.Join(' ', title, fileName)
        };
        db.Documents.Add(document);
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Document = document,
            Type = ProcessingJobType.OcrAndAnalyze,
            State = ProcessingJobState.Pending,
            Priority = 10,
            Attempts = 0,
            CreatedAt = now
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ImportResult.Succeeded(document.Id);
        }
        catch (DbUpdateException exception) when (DocumentPersistenceErrors.IsDuplicateHash(exception))
        {
            if (!stored.AlreadyExisted)
            {
                TryDeleteStored(stored.RelativePath, fileName);
            }

            logger.LogInformation("Skipped concurrently imported duplicate document {FileName}.", fileName);
            return ImportResult.Failed("Dieses Dokument ist bereits vorhanden.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!stored.AlreadyExisted)
            {
                TryDeleteStored(stored.RelativePath, fileName);
            }
            logger.LogError(exception, "Could not persist imported document {FileName}; stored file was removed.", fileName);
            return ImportResult.Failed("Das Dokument konnte nicht in der Datenbank angelegt werden.");
        }
    }

    private void TryDeleteStored(string relativePath, string fileName)
    {
        try
        {
            storage.Delete(relativePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not clean up stored document {FileName} at {RelativePath}.", fileName, relativePath);
        }
    }

}

public sealed record ImportResult(bool Success, long? DocumentId, string? Error)
{
    public static ImportResult Failed(string error) => new(false, null, error);

    public static ImportResult Succeeded(long documentId) => new(true, documentId, null);
}
