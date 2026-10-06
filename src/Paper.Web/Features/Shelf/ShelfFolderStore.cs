using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Storage;

namespace Paper.Web.Features.Shelf;

public sealed class ShelfFolderStore(AppDbContext db, TimeProvider timeProvider, IStorageProvider storage)
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
        var folders = await db.ShelfFolders.AsNoTracking()
            .Select(item => new ShelfFolderOption(item.Id, item.Name, item.RelativePath, item.ParentId))
            .ToListAsync(cancellationToken);
        var byId = folders.ToDictionary(item => item.Id);
        var breadcrumbs = new List<ShelfFolderOption>();
        for (var currentId = folder.Id; byId.TryGetValue(currentId, out var current); currentId = current.ParentId ?? 0)
        {
            breadcrumbs.Add(current);
            if (current.ParentId is null)
            {
                break;
            }
        }

        breadcrumbs.Reverse();
        return new ShelfFolderView(folder.Id, folder.Name, folder.RelativePath, folder.ParentId, documents, breadcrumbs);
    }

    public async Task<ShelfFolder?> CreateAsync(long? parentId, string name, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length is 0 or > 120 ||
            normalizedName.Contains('/') ||
            normalizedName.Contains('\\') ||
            normalizedName.Contains(':') ||
            normalizedName.Any(character => character < 32 || Path.GetInvalidFileNameChars().Contains(character)))
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

    public async Task<ShelfFolderUpdateResult> UpdateLocationAsync(long id, long? parentId, string name, CancellationToken cancellationToken)
    {
        var folder = await db.ShelfFolders.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (folder is null)
        {
            return ShelfFolderUpdateResult.Missing;
        }

        var normalizedName = name.Trim();
        if (normalizedName.Length is 0 or > 120 ||
            normalizedName.Contains('/') ||
            normalizedName.Contains('\\') ||
            normalizedName.Contains(':') ||
            normalizedName.Any(character => character < 32 || Path.GetInvalidFileNameChars().Contains(character)))
        {
            return ShelfFolderUpdateResult.Invalid("Der Ordnername ist ungültig.");
        }

        if (parentId == id)
        {
            return ShelfFolderUpdateResult.Invalid("Ein Ordner kann nicht sein eigener übergeordneter Ordner sein.");
        }

        var oldPath = folder.RelativePath;
        if (parentId is not null && await db.ShelfFolders.AnyAsync(item => item.Id == parentId && item.RelativePath.StartsWith(oldPath + "/"), cancellationToken))
        {
            return ShelfFolderUpdateResult.Invalid("Ein Ordner kann nicht in einen eigenen Unterordner verschoben werden.");
        }

        var parentPath = parentId is null
            ? null
            : await db.ShelfFolders.Where(item => item.Id == parentId).Select(item => item.RelativePath).SingleOrDefaultAsync(cancellationToken);
        if (parentId is not null && parentPath is null)
        {
            return ShelfFolderUpdateResult.Invalid("Der Zielordner existiert nicht.");
        }

        var newPath = parentPath is null ? normalizedName : $"{parentPath}/{normalizedName}";
        if (newPath.Equals(oldPath, StringComparison.Ordinal))
        {
            return ShelfFolderUpdateResult.Success;
        }

        if (await db.ShelfFolders.AnyAsync(item => item.Id != id && item.RelativePath == newPath, cancellationToken))
        {
            return ShelfFolderUpdateResult.Invalid("Am Ziel existiert bereits ein Ordner mit diesem Namen.");
        }

        var descendants = await db.ShelfFolders
            .Where(item => item.RelativePath.StartsWith(oldPath + "/"))
            .ToListAsync(cancellationToken);
        var documents = await db.Documents
            .Where(item => item.FilePath.StartsWith(oldPath + "/"))
            .ToListAsync(cancellationToken);
        var movedDirectory = false;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await storage.MoveDirectoryAsync(oldPath, newPath, cancellationToken);
            movedDirectory = true;
            folder.ParentId = parentId;
            folder.Name = normalizedName;
            folder.RelativePath = newPath;
            folder.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var descendant in descendants)
            {
                descendant.RelativePath = newPath + descendant.RelativePath[oldPath.Length..];
                descendant.UpdatedAt = folder.UpdatedAt;
            }

            foreach (var document in documents)
            {
                document.FilePath = newPath + document.FilePath[oldPath.Length..];
                document.SearchText = document.SearchText.Replace(oldPath, newPath, StringComparison.Ordinal);
                document.UpdatedAt = folder.UpdatedAt;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ShelfFolderUpdateResult.Success;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (movedDirectory)
            {
                try
                {
                    await storage.MoveDirectoryAsync(newPath, oldPath, CancellationToken.None);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("Die Regaländerung und ihr Rollback sind fehlgeschlagen.", rollbackError);
                }
            }

            throw;
        }
    }
}

public sealed record ShelfFolderOption(long Id, string Name, string RelativePath, long? ParentId);

public sealed record ShelfDocument(long Id, string Title, DateOnly? DocumentDate, string OriginalFileName, long FileSize);

public sealed record ShelfFolderView(long Id, string Name, string RelativePath, long? ParentId, IReadOnlyList<ShelfDocument> Documents, IReadOnlyList<ShelfFolderOption> Breadcrumbs);

public sealed record ShelfFolderUpdateResult(bool Succeeded, bool NotFound, string? Error)
{
    public static ShelfFolderUpdateResult Success { get; } = new(true, false, null);
    public static ShelfFolderUpdateResult Missing { get; } = new(false, true, null);
    public static ShelfFolderUpdateResult Invalid(string error) => new(false, false, error);
}
