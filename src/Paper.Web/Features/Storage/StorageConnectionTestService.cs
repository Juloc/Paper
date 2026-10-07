namespace Paper.Web.Features.Storage;

public sealed class StorageConnectionTestService(StorageConfigurationStore configurations, StorageProviderFactory providers)
{
    public async Task<StorageConnectionTestResult> TestAsync(StorageConfigurationEdit edit, CancellationToken cancellationToken)
    {
        var validationError = StorageConfigurationValidator.Validate(edit);
        if (validationError is not null)
        {
            return StorageConnectionTestResult.Failed(validationError);
        }

        try
        {
            var options = configurations.CreateProviderOptions(edit);
            if (edit.ProviderType == StorageProviderType.Smb && string.IsNullOrWhiteSpace(edit.SmbPassword))
            {
                options.SmbPassword = configurations.LoadProviderOptions().SmbPassword;
            }

            var provider = providers.Create(options);
            return await provider.TestConnectionAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return StorageConnectionTestResult.Failed(exception.Message);
        }
    }
}

public sealed record StorageConnectionTestResult(bool Succeeded, string Message)
{
    public static StorageConnectionTestResult Failed(string message) => new(false, message);
}
