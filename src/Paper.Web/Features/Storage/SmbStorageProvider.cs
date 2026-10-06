namespace Paper.Web.Features.Storage;

/// <summary>
/// SMB provider backed by the operating system's native UNC/filesystem support.
/// Credentials and connection lifecycle stay outside the application; on Linux
/// the configured root must be an SMB mount exposed to the container.
/// </summary>
public sealed class SmbStorageProvider(LocalDocumentStorage fileSystemStorage) : IStorageProvider
{
    public Task<StoredDocument> SaveAsync(Stream source, string originalFileName, CancellationToken cancellationToken) =>
        fileSystemStorage.SaveAsync(source, originalFileName, cancellationToken);

    public Task<string> MoveToShelfAsync(
        string sourceRelativePath,
        string shelfRelativePath,
        DateOnly? documentDate,
        string title,
        string originalFileName,
        CancellationToken cancellationToken) =>
        fileSystemStorage.MoveToShelfAsync(sourceRelativePath, shelfRelativePath, documentDate, title, originalFileName, cancellationToken);

    public Task MoveAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken) =>
        fileSystemStorage.MoveAsync(sourceRelativePath, destinationRelativePath, cancellationToken);

    public Task MoveBackAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken) =>
        fileSystemStorage.MoveBackAsync(sourceRelativePath, destinationRelativePath, cancellationToken);

    public Task MoveDirectoryAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken) =>
        fileSystemStorage.MoveDirectoryAsync(sourceRelativePath, destinationRelativePath, cancellationToken);

    public bool DirectoryExists(string relativePath) => fileSystemStorage.DirectoryExists(relativePath);

    public void EnsureDirectory(string relativePath) => fileSystemStorage.EnsureDirectory(relativePath);

    public string GetSafePath(string relativePath) => fileSystemStorage.GetSafePath(relativePath);

    public FileStream OpenRead(string relativePath) => fileSystemStorage.OpenRead(relativePath);

    public void Delete(string relativePath) => fileSystemStorage.Delete(relativePath);
}
