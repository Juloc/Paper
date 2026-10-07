using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Storage;
using SkiaSharp;

namespace Paper.Web.Features.Documents;

public sealed class ThumbnailService(AppDbContext db, IStorageProvider storage, ILogger<ThumbnailService> logger)
{
    private const int MaximumDimension = 480;
    private static readonly HashSet<string> ImageExtensions = [".jpg", ".jpeg", ".png", ".tif", ".tiff"];

    public async Task<ThumbnailFile?> OpenAsync(long id, CancellationToken cancellationToken)
    {
        try
        {
            var document = await db.Documents.AsNoTracking()
                .Select(item => new { item.Id, item.Hash, item.FilePath, item.OriginalFileName })
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (document is null || !ImageExtensions.Contains(Path.GetExtension(document.OriginalFileName).ToLowerInvariant()))
            {
                return null;
            }

            var relativePath = $".thumbnails/{document.Hash}.jpg";
            if (!storage.TryGetLocalPath(relativePath, out var thumbnailPath))
            {
                var bytes = await RenderAsync(document.FilePath, cancellationToken);
                return bytes is null
                    ? null
                    : new ThumbnailFile(new MemoryStream(bytes, writable: false), "image/jpeg");
            }

            if (!File.Exists(thumbnailPath))
            {
                await CreateAsync(document.FilePath, thumbnailPath, cancellationToken);
            }

            return File.Exists(thumbnailPath)
                ? new ThumbnailFile(new FileStream(thumbnailPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true), "image/jpeg")
                : null;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not open thumbnail for document {DocumentId}.", id);
            return null;
        }
    }

    private async Task CreateAsync(string sourceRelativePath, string destinationPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var bytes = await RenderAsync(sourceRelativePath, cancellationToken);
            if (bytes is null)
            {
                return;
            }

            await using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
            {
                await target.WriteAsync(bytes, cancellationToken);
            }

            if (!File.Exists(destinationPath))
            {
                File.Move(temporaryPath, destinationPath);
            }
        }
        catch (IOException) when (File.Exists(destinationPath))
        {
            // Another request created the same content concurrently.
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not create thumbnail for {SourcePath}.", sourceRelativePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<byte[]?> RenderAsync(string sourceRelativePath, CancellationToken cancellationToken)
    {
        await using var source = storage.OpenRead(sourceRelativePath);
        using var bitmap = await Task.Run(() => SKBitmap.Decode(source), cancellationToken);
        if (bitmap is null)
        {
            return null;
        }

        var scale = Math.Min(1d, Math.Min((double)MaximumDimension / bitmap.Width, (double)MaximumDimension / bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        using var resized = bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized is null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 82);
        return data.ToArray();
    }
}

public sealed record ThumbnailFile(Stream Stream, string ContentType);
