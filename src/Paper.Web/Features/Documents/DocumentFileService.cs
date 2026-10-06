using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Storage;

namespace Paper.Web.Features.Documents;

public sealed class DocumentFileService(AppDbContext db, IStorageProvider storage)
{
    public async Task<DocumentFile?> OpenAsync(long id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var contentType = Path.GetExtension(document.OriginalFileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".tif" or ".tiff" => "image/tiff",
            _ => "application/octet-stream"
        };
        return new DocumentFile(storage.OpenRead(document.FilePath), contentType, document.OriginalFileName);
    }
}

public sealed record DocumentFile(FileStream Stream, string ContentType, string DownloadName);
