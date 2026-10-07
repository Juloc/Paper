namespace Paper.Web.Features.Storage;

public sealed class StorageProviderFactory(ILoggerFactory loggerFactory)
{
    public IStorageProvider Create(StorageOptions options) => options.Provider.Equals("smb", StringComparison.OrdinalIgnoreCase)
        ? new SmbStorageProvider(options, loggerFactory.CreateLogger<SmbStorageProvider>())
        : new LocalDocumentStorage(options, loggerFactory.CreateLogger<LocalDocumentStorage>());
}
