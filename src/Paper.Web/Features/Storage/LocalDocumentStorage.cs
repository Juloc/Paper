using System.Security.Cryptography;

namespace Paper.Web.Features.Storage;

public sealed record StoredDocument(string RelativePath, string Hash, long Size);

public sealed class LocalDocumentStorage(IConfiguration configuration, ILogger<LocalDocumentStorage> logger)
{
    private readonly string rootPath = Path.GetFullPath(configuration["Storage:RootPath"] ?? "/data/documents");

    public async Task<(string Path, StoredDocument Stored)> SaveAsync(Stream source, string originalFileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var temporaryDirectory = Path.Combine(rootPath, ".incoming");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.upload");

        try
        {
            await using (var temporary = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[64 * 1024];
                long size = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await temporary.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    size += read;
                }

                var hashValue = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                var relativePath = Path.Combine(hashValue[..2], hashValue + extension);
                var finalPath = GetSafePath(relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                if (File.Exists(finalPath))
                {
                    File.Delete(temporaryPath);
                    return (relativePath, new StoredDocument(relativePath, hashValue, size));
                }

                temporary.Close();
                File.Move(temporaryPath, finalPath);
                return (relativePath, new StoredDocument(relativePath, hashValue, size));
            }
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public string GetSafePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("A relative storage path is required.", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var rootWithSeparator = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The storage path is outside the document root.");
        }

        return fullPath;
    }

    public FileStream OpenRead(string relativePath)
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

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
