using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.Shelf;

public sealed class ShelfFolderStore(AppDbContext db, TimeProvider timeProvider)
{
    public Task<List<ShelfFolderOption>> ListOptionsAsync(CancellationToken cancellationToken) =>
        db.ShelfFolders.AsNoTracking()
            .OrderBy(folder => folder.RelativePath)
            .Select(folder => new ShelfFolderOption(folder.Id, folder.Name, folder.RelativePath, folder.ParentId))
            .ToListAsync(cancellationToken);

    public async Task<ShelfFolderView?> GetAsync(long id, CancellationToken cancellationToken)
    {
        var folder = await db.ShelfFolders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (folder is null)
        {
            return null;
        }

        var documents = await db.Documents.AsNoTracking()
            .Where(document => document.ShelfFolderId == id)
            .OrderByDescending(document => document.DocumentDate)
            .ThenByDescending(document => document.UpdatedAt)
            .Select(document => new ShelfDocument(
                document.Id,
                document.Title,
                document.DocumentDate,
                document.OriginalFileName,
                document.FileSize))
            .ToListAsync(cancellationToken);
        return new ShelfFolderView(folder.Id, folder.Name, folder.RelativePath, folder.ParentId, documents);
    }

    public async Task<ShelfFolder?> CreateAsync(long? parentId, string name, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length is 0 or > 120 || normalizedName.Contains('/') || normalizedName.Contains('\\'))
        {
            return null;
        }

        string relativePath;
        if (parentId is null)
        {
            relativePath = normalizedName;
        }
        else
        {
            var parent = await db.ShelfFolders.SingleOrDefaultAsync(folder => folder.Id == parentId, cancellationToken);
            if (parent is null)
            {
                return null;
            }

            relativePath = $"{parent.RelativePath}/{normalizedName}";
        }

        if (await db.ShelfFolders.AnyAsync(folder => folder.RelativePath == relativePath, cancellationToken))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var folderEntity = new ShelfFolder
        {
            ParentId = parentId,
            Name = normalizedName,
            RelativePath = relativePath,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ShelfFolders.Add(folderEntity);
        await db.SaveChangesAsync(cancellationToken);
        return folderEntity;
    }
}

public sealed record ShelfFolderOption(long Id, string Name, string RelativePath, long? ParentId);

public sealed record ShelfDocument(long Id, string Title, DateOnly? DocumentDate, string OriginalFileName, long FileSize);

public sealed record ShelfFolderView(long Id, string Name, string RelativePath, long? ParentId, IReadOnlyList<ShelfDocument> Documents);
