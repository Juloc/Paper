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
                documents.Select(document => new DocumentBackupManifestEntry(
                    document.Id,
                    document.Title,
                    document.DocumentDate,
                    document.OriginalFileName,
                    document.FilePath,
                    document.FileSize,
                    document.Hash,
                    document.Status.ToString(),
                    document.OcrStatus.ToString(),
                    document.OcrText,
                    document.OcrError,
                    document.CreatedAt,
                    document.UpdatedAt,
                    document.Correspondent?.Name,
                    document.DocumentType?.Name,
                    document.ShelfFolder?.RelativePath,
                    document.Tags.Select(link => link.Tag.Name).OrderBy(name => name).ToArray(),
                    document.CustomFields
                        .OrderBy(field => field.CustomField.Name)
                        .Select(field => new DocumentBackupCustomField(field.CustomField.Name, field.CustomField.Type.ToString(), field.Value))
                        .ToArray())),
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

public sealed record DocumentBackupManifestEntry(
    long Id,
    string Title,
    DateOnly? DocumentDate,
    string OriginalFileName,
    string FilePath,
    long FileSize,
    string Hash,
    string Status,
    string OcrStatus,
    string? OcrText,
    string? OcrError,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? Correspondent,
    string? DocumentType,
    string? ShelfPath,
    string[] Tags,
    DocumentBackupCustomField[] CustomFields);

public sealed record DocumentBackupCustomField(string Name, string Type, string Value);
