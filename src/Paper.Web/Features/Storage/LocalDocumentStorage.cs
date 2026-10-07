using System.Security.Cryptography;

namespace Paper.Web.Features.Storage;

public sealed record StoredDocument(string RelativePath, string Hash, long Size, bool AlreadyExisted = false);
public sealed record StorageFileMetadata(long Length);

public interface IStorageProvider
{
    Task<StoredDocument> SaveAsync(Stream source, string originalFileName, CancellationToken cancellationToken);
    Task<string> MoveToShelfAsync(string sourceRelativePath, string shelfRelativePath, DateOnly? documentDate, string title, string originalFileName, CancellationToken cancellationToken);
    Task MoveAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken);
    Task MoveBackAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken);
    Task MoveDirectoryAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken);
    bool DirectoryExists(string relativePath);
    bool FileExists(string relativePath);
    StorageFileMetadata? GetFileMetadata(string relativePath);
    void EnsureDirectory(string relativePath);
    bool TryGetLocalPath(string relativePath, out string path);
    Stream OpenRead(string relativePath);
    void Delete(string relativePath);
}

public sealed class LocalDocumentStorage : IStorageProvider
{
    private readonly ILogger<LocalDocumentStorage> logger;
    private readonly StorageOptions options;
    private readonly string rootPath;

    public LocalDocumentStorage(IConfiguration configuration, ILogger<LocalDocumentStorage> logger)
    {
        this.logger = logger;
        options = configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
        rootPath = Path.GetFullPath(options.EffectiveRootPath());
    }

    public async Task<StoredDocument> SaveAsync(Stream source, string originalFileName, CancellationToken cancellationToken)
    {
        var temporaryDirectory = Path.Combine(rootPath, ".incoming");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.upload");

        try
        {
            long size;
            string hashValue;
            await using (var temporary = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[64 * 1024];
                size = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await temporary.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    size += read;
                }

                hashValue = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            var relativePath = StoragePathPolicy.CreateInboxPath(hashValue, originalFileName);
            var finalPath = GetSafePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            var alreadyExisted = File.Exists(finalPath);
            if (!alreadyExisted)
            {
                File.Move(temporaryPath, finalPath);
            }
            else
            {
                File.Delete(temporaryPath);
            }

            return new StoredDocument(relativePath, hashValue, size, alreadyExisted);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public async Task<string> MoveToShelfAsync(
        string sourceRelativePath,
        string shelfRelativePath,
        DateOnly? documentDate,
        string title,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        var sourcePath = GetSafePath(sourceRelativePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Die Dokumentdatei wurde nicht gefunden.", sourcePath);
        }

        var folderPath = StoragePathPolicy.NormalizeFolderPath(shelfRelativePath);
        var directoryPath = GetSafePath(folderPath);
        Directory.CreateDirectory(directoryPath);
        var fileName = StoragePathPolicy.CreateShelfFileName(documentDate, title, originalFileName);
        var destinationRelativePath = GetAvailablePath(folderPath, fileName, sourceRelativePath);
        var destinationPath = GetSafePath(destinationRelativePath);
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            return destinationRelativePath;
        }

        await Task.Run(() => File.Move(sourcePath, destinationPath), cancellationToken);
        logger.LogInformation("Moved document from {SourcePath} to {DestinationPath}.", sourceRelativePath, destinationRelativePath);
        return destinationRelativePath;
    }

    public async Task MoveAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken)
    {
        var sourcePath = GetSafePath(sourceRelativePath);
        var destinationPath = GetSafePath(destinationRelativePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Die Dokumentdatei wurde nicht gefunden.", sourcePath);
        }

        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            throw new IOException("Der Zieldateipfad existiert bereits.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await Task.Run(() => File.Move(sourcePath, destinationPath), cancellationToken);
    }

    public Task MoveBackAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken) =>
        MoveAsync(sourceRelativePath, destinationRelativePath, cancellationToken);

    public async Task MoveDirectoryAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken)
    {
        var sourcePath = GetSafePath(sourceRelativePath);
        var destinationPath = GetSafePath(destinationRelativePath);
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Der Regalordner wurde nicht gefunden: {sourceRelativePath}");
        }

        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
        {
            throw new IOException("Der Zielordner existiert bereits.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await Task.Run(() => Directory.Move(sourcePath, destinationPath), cancellationToken);
    }

    public bool DirectoryExists(string relativePath) => Directory.Exists(GetSafePath(relativePath));

    public bool FileExists(string relativePath) => File.Exists(GetSafePath(relativePath));

    public StorageFileMetadata? GetFileMetadata(string relativePath)
    {
        var info = new FileInfo(GetSafePath(relativePath));
        return info.Exists ? new StorageFileMetadata(info.Length) : null;
    }

    public void EnsureDirectory(string relativePath) => Directory.CreateDirectory(GetSafePath(relativePath));

    public string GetSafePath(string relativePath)
    {
        var normalizedPath = StoragePathPolicy.NormalizeRelativePath(relativePath);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Der Dateipfad liegt außerhalb des Dokumentenspeichers.");
        }

        return fullPath;
    }

    public bool TryGetLocalPath(string relativePath, out string path)
    {
        path = GetSafePath(relativePath);
        return true;
    }

    public Stream OpenRead(string relativePath)
    {
        var path = GetSafePath(relativePath);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
    }

    public void Delete(string relativePath)
    {
        var path = GetSafePath(relativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
            logger.LogInformation("Deleted document file {Path}.", relativePath);
        }
    }

    private string GetAvailablePath(string folderPath, string fileName, string sourceRelativePath)
    {
        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var attempt = 1;
        var relativePath = StoragePathPolicy.Combine(folderPath, fileName);
        while (File.Exists(GetSafePath(relativePath)) &&
               !string.Equals(relativePath, StoragePathPolicy.NormalizeRelativePath(sourceRelativePath), StringComparison.OrdinalIgnoreCase))
        {
            attempt++;
            relativePath = StoragePathPolicy.Combine(folderPath, $"{stem} ({attempt}){extension}");
        }

        return relativePath;
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
