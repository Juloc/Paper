namespace Paper.Web.Features.Storage;

public sealed class StorageOptions
{
    public string Provider { get; set; } = "local";

    public string RootPath { get; set; } = "/data/documents";

    public string? SmbRootPath { get; set; }

    public string? SmbUsername { get; set; }

    public string? SmbPassword { get; set; }

    public string? SmbDomain { get; set; }

    public WakePolicy WakePolicy { get; set; } = WakePolicy.Never;

    public string? WakeMacAddress { get; set; }

    public string WakeBroadcastAddress { get; set; } = "255.255.255.255";

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

public sealed record SmbEndpoint(string Server, string Share, string BasePath)
{
    public static SmbEndpoint Parse(string value)
    {
        var normalized = value.Trim().Replace('\\', '/');
        if (normalized.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[6..];
        }
        else if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        else
        {
            throw new InvalidOperationException("Der SMB-Pfad muss smb://server/share oder \\\\server\\share verwenden.");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2 || segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            throw new InvalidOperationException("Der SMB-Pfad enthält keinen gültigen Server und Share.");
        }

        return new SmbEndpoint(segments[0], segments[1], string.Join('/', segments.Skip(2)));
    }

    public static SmbEndpoint Create(string server, string share, string? basePath)
    {
        var normalizedServer = server.Trim();
        var normalizedShare = share.Trim().Trim('/','\\');
        var normalizedBasePath = string.Join('/', (basePath ?? string.Empty).Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (string.IsNullOrWhiteSpace(normalizedServer) || string.IsNullOrWhiteSpace(normalizedShare) || normalizedServer.Contains('/') || normalizedServer.Contains('\\') || normalizedShare.Contains('/') || normalizedShare.Contains('\\'))
        {
            throw new InvalidOperationException("Server und Freigabe müssen gültige SMB-Pfade sein.");
        }

        if (normalizedBasePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            throw new InvalidOperationException("Der SMB-Unterordner ist ungültig.");
        }

        return new SmbEndpoint(normalizedServer, normalizedShare, normalizedBasePath);
    }

    public string ToRootPath() => string.IsNullOrWhiteSpace(BasePath) ? $"smb://{Server}/{Share}" : $"smb://{Server}/{Share}/{BasePath}";
}
