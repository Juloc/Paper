using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Storage;

namespace Paper.Web.Features.Documents;

public sealed class DocumentImportService(AppDbContext db, LocalDocumentStorage storage, TimeProvider timeProvider)
{
    public async Task<ImportResult> ImportAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > DocumentInputValidator.MaximumFileSize || file.Length <= 0)
        {
            return ImportResult.Failed("Die Datei ist leer oder größer als 50 MB.");
        }

        await using var input = file.OpenReadStream();
        var header = new byte[8];
        var headerLength = await input.ReadAsync(header, cancellationToken);
        if (!DocumentInputValidator.TryValidate(file.FileName, file.ContentType, file.Length, header.AsSpan(0, headerLength), out _, out var error))
        {
            return ImportResult.Failed(error);
        }

        input.Position = 0;
        var stored = await storage.SaveAsync(input, file.FileName, cancellationToken);
        var duplicate = await db.Documents.AsNoTracking().AnyAsync(document => document.Hash == stored.Hash, cancellationToken);
        if (duplicate)
        {
            storage.Delete(stored.RelativePath);
            return ImportResult.Failed("Dieses Dokument ist bereits vorhanden.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var document = new Document
        {
            Title = Path.GetFileNameWithoutExtension(file.FileName).Trim(),
            OriginalFileName = Path.GetFileName(file.FileName),
            FilePath = stored.RelativePath,
            FileSize = stored.Size,
            Hash = stored.Hash,
            OcrStatus = OcrStatus.Pending,
            Status = DocumentStatus.Inbox,
            CreatedAt = now,
            UpdatedAt = now,
            SearchText = Path.GetFileNameWithoutExtension(file.FileName).Trim()
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
        await db.SaveChangesAsync(cancellationToken);
        return ImportResult.Succeeded(document.Id);
    }
}

public sealed record ImportResult(bool Success, long? DocumentId, string? Error)
{
    public static ImportResult Failed(string error) => new(false, null, error);

    public static ImportResult Succeeded(long documentId) => new(true, documentId, null);
}
