using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Storage;

namespace Paper.Web.Features.Export;

public sealed class DocumentBackupService(AppDbContext db, LocalDocumentStorage storage)
{
    public async Task WriteZipAsync(Stream destination, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var documents = await db.Documents
            .AsNoTracking()
            .Include(document => document.Correspondent)
            .Include(document => document.DocumentType)
            .Include(document => document.ShelfFolder)
            .Include(document => document.Tags).ThenInclude(link => link.Tag)
            .Include(document => document.CustomFields).ThenInclude(field => field.CustomField)
            .OrderBy(document => document.Id)
            .ToListAsync(cancellationToken);

        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
        await using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(
                manifestStream,
                documents.Select(document => new
                {
                    document.Id,
                    document.Title,
                    document.DocumentDate,
                    document.OriginalFileName,
                    document.FilePath,
                    document.FileSize,
                    document.Hash,
                    Status = document.Status.ToString(),
                    OcrStatus = document.OcrStatus.ToString(),
                    Correspondent = document.Correspondent?.Name,
                    DocumentType = document.DocumentType?.Name,
                    ShelfPath = document.ShelfFolder?.RelativePath,
                    Tags = document.Tags.Select(link => link.Tag.Name).OrderBy(name => name).ToArray(),
                    CustomFields = document.CustomFields
                        .OrderBy(field => field.CustomField.Name)
                        .Select(field => new { Name = field.CustomField.Name, Type = field.CustomField.Type.ToString(), field.Value })
                        .ToArray()
                }),
                cancellationToken: cancellationToken);
        }

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryName = $"documents/{document.Id:D8}_{StoragePathPolicy.SanitizeFileName(document.OriginalFileName)}";
            var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
            await using var target = entry.Open();
            await using var source = storage.OpenRead(document.FilePath);
            await source.CopyToAsync(target, cancellationToken);
        }
    }
}
