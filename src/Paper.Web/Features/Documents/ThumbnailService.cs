using System.ComponentModel;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Storage;
using SkiaSharp;

namespace Paper.Web.Features.Documents;

public sealed class ThumbnailService(
    AppDbContext db,
    IStorageProvider storage,
    TimeProvider timeProvider,
    ILogger<ThumbnailService> logger)
{
    private const int MaximumDimension = 480;
    private const int MaximumSourceDimension = 12_000;
    private const long MaximumSourcePixels = 25_000_000;
    private static readonly HashSet<string> ImageExtensions = [".jpg", ".jpeg", ".png", ".tif", ".tiff"];

    public async Task<ThumbnailFile?> OpenAsync(long id, CancellationToken cancellationToken)
    {
        var thumbnail = await db.DocumentThumbnails.AsNoTracking()
            .Where(item => item.DocumentId == id)
            .Select(item => new
            {
                item.ContentType,
                item.Data,
                item.CreatedAt,
                Hash = item.Document.Hash
            })
            .SingleOrDefaultAsync(cancellationToken);
        return thumbnail is null
            ? null
            : new ThumbnailFile(
                new MemoryStream(thumbnail.Data, writable: false),
                thumbnail.ContentType,
                $"{thumbnail.Hash}-{thumbnail.CreatedAt.Ticks}");
    }

    public async Task<bool> QueueAsync(long documentId, CancellationToken cancellationToken)
    {
        if (!await db.Documents.AnyAsync(document => document.Id == documentId, cancellationToken))
        {
            return false;
        }

        var existing = await db.ProcessingJobs
            .SingleOrDefaultAsync(job => job.DocumentId == documentId && job.Type == ProcessingJobType.Thumbnail, cancellationToken);
        if (existing is not null)
        {
            if (existing.State is ProcessingJobState.Pending or ProcessingJobState.Running or ProcessingJobState.Succeeded)
            {
                return true;
            }

            existing.State = ProcessingJobState.Pending;
            existing.Attempts = 0;
            existing.StartedAt = null;
            existing.FinishedAt = null;
            existing.Error = null;
        }
        else
        {
            db.ProcessingJobs.Add(new ProcessingJob
            {
                DocumentId = documentId,
                Type = ProcessingJobType.Thumbnail,
                State = ProcessingJobState.Pending,
                Priority = 5,
                Attempts = 0,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> QueueMissingAsync(CancellationToken cancellationToken)
    {
        var documentIds = await db.Documents.AsNoTracking()
            .Where(document => !db.DocumentThumbnails.Any(thumbnail => thumbnail.DocumentId == document.Id))
            .Where(document => !db.ProcessingJobs.Any(job =>
                job.DocumentId == document.Id &&
                job.Type == ProcessingJobType.Thumbnail &&
                (job.State == ProcessingJobState.Pending || job.State == ProcessingJobState.Running || job.State == ProcessingJobState.Succeeded)))
            .Select(document => document.Id)
            .ToListAsync(cancellationToken);
        if (documentIds.Count == 0)
        {
            return 0;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        db.ProcessingJobs.AddRange(documentIds.Select(documentId => new ProcessingJob
        {
            DocumentId = documentId,
            Type = ProcessingJobType.Thumbnail,
            State = ProcessingJobState.Pending,
            Priority = 5,
            Attempts = 0,
            CreatedAt = now
        }));
        await db.SaveChangesAsync(cancellationToken);
        return documentIds.Count;
    }

    public async Task GenerateAsync(long documentId, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking()
            .Where(item => item.Id == documentId)
            .Select(item => new { item.FilePath, item.OriginalFileName })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new FileNotFoundException("Das Dokument für das Thumbnail wurde nicht gefunden.");

        var rendered = await RenderAsync(document.FilePath, document.OriginalFileName, cancellationToken)
            ?? throw new InvalidOperationException("Die erste Dokumentseite konnte nicht als Thumbnail gerendert werden.");
        var existing = await db.DocumentThumbnails.SingleOrDefaultAsync(item => item.DocumentId == documentId, cancellationToken);
        if (existing is null)
        {
            db.DocumentThumbnails.Add(new DocumentThumbnail
            {
                DocumentId = documentId,
                ContentType = rendered.ContentType,
                Width = rendered.Width,
                Height = rendered.Height,
                Data = rendered.Data,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime
            });
        }
        else
        {
            existing.ContentType = rendered.ContentType;
            existing.Width = rendered.Width;
            existing.Height = rendered.Height;
            existing.Data = rendered.Data;
            existing.CreatedAt = timeProvider.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<RenderedThumbnail?> RenderAsync(string sourceRelativePath, string originalFileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (extension == ".pdf")
        {
            var pdfBytes = await RenderPdfFirstPageAsync(sourceRelativePath, cancellationToken);
            return pdfBytes is null ? null : Encode(pdfBytes);
        }

        if (!ImageExtensions.Contains(extension))
        {
            return null;
        }

        await using var source = storage.OpenRead(sourceRelativePath);
        using var codec = SKCodec.Create(source);
        if (codec is null || !IsSafe(codec.Info.Width, codec.Info.Height))
        {
            logger.LogWarning("Skipped thumbnail for {SourcePath} because its decoded dimensions exceed the safety limit.", sourceRelativePath);
            return null;
        }

        using var bitmap = await Task.Run(() => SKBitmap.Decode(codec), cancellationToken);
        return bitmap is null ? null : Encode(bitmap);
    }

    private async Task<byte[]?> RenderPdfFirstPageAsync(string sourceRelativePath, CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "paper-thumbnails");
        Directory.CreateDirectory(tempDirectory);
        var inputPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.pdf");
        var outputPrefix = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N"));
        var outputPath = outputPrefix + ".jpg";
        try
        {
            await using (var source = storage.OpenRead(sourceRelativePath))
            await using (var target = new FileStream(inputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024, useAsync: true))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            var startInfo = new ProcessStartInfo("pdftoppm")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-singlefile");
            startInfo.ArgumentList.Add("-jpeg");
            startInfo.ArgumentList.Add("-scale-to");
            startInfo.ArgumentList.Add(MaximumDimension.ToString());
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add(outputPrefix);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("pdftoppm konnte nicht gestartet werden.");
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                logger.LogWarning("PDF thumbnail rendering failed for {SourcePath}: {Error}", sourceRelativePath, error);
                return null;
            }

            return await File.ReadAllBytesAsync(outputPath, cancellationToken);
        }
        catch (Exception exception) when (exception is FileNotFoundException or Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(exception, "PDF thumbnail rendering is unavailable for {SourcePath}.", sourceRelativePath);
            return null;
        }
        finally
        {
            TryDelete(inputPath);
            TryDelete(outputPath);
        }
    }

    private RenderedThumbnail Encode(byte[] encodedImage)
    {
        using var bitmap = SKBitmap.Decode(encodedImage);
        return bitmap is null ? throw new InvalidOperationException("Das gerenderte Bild ist ungültig.") : Encode(bitmap);
    }

    private RenderedThumbnail Encode(SKBitmap bitmap)
    {
        var scale = Math.Min(1d, Math.Min((double)MaximumDimension / bitmap.Width, (double)MaximumDimension / bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        using var resized = bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized is null)
        {
            throw new InvalidOperationException("Das Thumbnail konnte nicht skaliert werden.");
        }

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 82);
        return new RenderedThumbnail(data.ToArray(), width, height, "image/jpeg");
    }

    private static bool IsSafe(int width, int height) =>
        width > 0 && height > 0 && width <= MaximumSourceDimension && height <= MaximumSourceDimension && (long)width * height <= MaximumSourcePixels;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed record RenderedThumbnail(byte[] Data, int Width, int Height, string ContentType);
}

public sealed record ThumbnailFile(Stream Stream, string ContentType, string EntityTag);
