namespace Paper.Web.Features.Storage;

public sealed class StorageOptions
{
    public string Provider { get; set; } = "local";
    public string RootPath { get; set; } = "/data/documents";
    public string? SmbRootPath { get; set; }
    public WakePolicy WakePolicy { get; set; } = WakePolicy.Never;

    public string EffectiveRootPath()
    {
        if (Provider.Equals("smb", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(SmbRootPath))
            {
                throw new InvalidOperationException("Storage:SmbRootPath ist für den SMB-Provider erforderlich.");
            }

            return SmbRootPath;
        }

        if (!Provider.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unbekannter Storage-Provider: {Provider}.");
        }

        return RootPath;
    }
}

public enum WakePolicy
{
    Never,
    OnDemand
}
