using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.Storage;

public enum StorageProviderType
{
    Local,
    Smb
}

public sealed class StorageConfiguration
{
    public long Id { get; set; }

    public StorageProviderType ProviderType { get; set; }

    public string LocalRootPath { get; set; } = "/data/documents";

    public string? SmbServer { get; set; }

    public string? SmbShare { get; set; }

    public string? SmbBasePath { get; set; }

    public string? SmbUsername { get; set; }

    public string? EncryptedSmbPassword { get; set; }

    public string? SmbDomain { get; set; }

    public WakePolicy WakePolicy { get; set; } = WakePolicy.Never;

    public string? WakeMacAddress { get; set; }

    public string WakeBroadcastAddress { get; set; } = "255.255.255.255";

    public DateTime UpdatedAt { get; set; }
}

public sealed record StorageConfigurationView(
    StorageProviderType ProviderType,
    string LocalRootPath,
    string? SmbServer,
    string? SmbShare,
    string? SmbBasePath,
    string? SmbUsername,
    string? SmbDomain,
    bool HasSmbPassword,
    WakePolicy WakePolicy,
    string? WakeMacAddress,
    string WakeBroadcastAddress,
    DateTime UpdatedAt);

public sealed record StorageConfigurationEdit(
    StorageProviderType ProviderType,
    string LocalRootPath,
    string? SmbServer,
    string? SmbShare,
    string? SmbBasePath,
    string? SmbUsername,
    string? SmbPassword,
    string? SmbDomain,
    WakePolicy WakePolicy,
    string? WakeMacAddress,
    string WakeBroadcastAddress);

public sealed record StorageSaveResult(bool Saved, bool Blocked, string? Error = null)
{
    public static StorageSaveResult Success => new(true, false);

    public static StorageSaveResult BlockedByExistingDocuments => new(false, true, "Der Speicher kann nicht gewechselt werden, solange Dokumente vorhanden sind. Leere den aktuellen Speicher oder migriere die Dateien zuerst.");

    public static StorageSaveResult Invalid(string error) => new(false, false, error);
}

public sealed class StorageConfigurationStore(
    AppDbContext db,
    IDataProtectionProvider dataProtection,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    public const long ActiveConfigurationId = 1;

    private readonly IDataProtector passwordProtector = dataProtection.CreateProtector("Paper.Storage.SmbPassword.v1");

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (await db.StorageConfigurations.AnyAsync(cancellationToken))
        {
            return;
        }

        var edit = FromBootstrapConfiguration(configuration);
        var validationError = StorageConfigurationValidator.Validate(edit);
        if (validationError is not null)
        {
            throw new InvalidOperationException($"Die initiale Storage-Konfiguration ist ungültig: {validationError}");
        }

        db.StorageConfigurations.Add(CreateEntity(edit, null));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<StorageConfigurationView> GetAsync(CancellationToken cancellationToken)
    {
        var entity = await db.StorageConfigurations.AsNoTracking().SingleAsync(item => item.Id == ActiveConfigurationId, cancellationToken);
        return ToView(entity);
    }

    public StorageOptions LoadProviderOptions()
    {
        var entity = db.StorageConfigurations.AsNoTracking().Single(item => item.Id == ActiveConfigurationId);
        return ToOptions(entity);
    }

    public async Task<StorageSaveResult> SaveAsync(StorageConfigurationEdit edit, CancellationToken cancellationToken)
    {
        var validationError = StorageConfigurationValidator.Validate(edit);
        if (validationError is not null)
        {
            return StorageSaveResult.Invalid(validationError);
        }

        var entity = await db.StorageConfigurations.SingleAsync(item => item.Id == ActiveConfigurationId, cancellationToken);
        var documentsExist = await db.Documents.AnyAsync(cancellationToken);
        if (documentsExist && !MatchesStorageLocation(entity, edit))
        {
            return StorageSaveResult.BlockedByExistingDocuments;
        }

        var encryptedPassword = string.IsNullOrWhiteSpace(edit.SmbPassword)
            ? entity.EncryptedSmbPassword
            : passwordProtector.Protect(edit.SmbPassword);
        entity.ProviderType = edit.ProviderType;
        entity.LocalRootPath = edit.LocalRootPath.Trim();
        entity.SmbServer = NormalizeOptional(edit.SmbServer);
        entity.SmbShare = NormalizeOptional(edit.SmbShare);
        entity.SmbBasePath = NormalizeOptional(edit.SmbBasePath);
        entity.SmbUsername = NormalizeOptional(edit.SmbUsername);
        entity.EncryptedSmbPassword = encryptedPassword;
        entity.SmbDomain = NormalizeOptional(edit.SmbDomain);
        entity.WakePolicy = edit.WakePolicy;
        entity.WakeMacAddress = NormalizeOptional(edit.WakeMacAddress);
        entity.WakeBroadcastAddress = edit.WakeBroadcastAddress.Trim();
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return StorageSaveResult.Success;
    }

    public StorageOptions CreateProviderOptions(StorageConfigurationEdit edit)
    {
        var validationError = StorageConfigurationValidator.Validate(edit);
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        return new StorageOptions
        {
            Provider = edit.ProviderType == StorageProviderType.Smb ? "smb" : "local",
            RootPath = edit.LocalRootPath.Trim(),
            SmbRootPath = edit.ProviderType == StorageProviderType.Smb ? SmbEndpoint.Create(edit.SmbServer!, edit.SmbShare!, edit.SmbBasePath).ToRootPath() : null,
            SmbUsername = edit.SmbUsername?.Trim(),
            SmbPassword = edit.SmbPassword,
            SmbDomain = edit.SmbDomain?.Trim(),
            WakePolicy = edit.WakePolicy,
            WakeMacAddress = edit.WakeMacAddress?.Trim(),
            WakeBroadcastAddress = edit.WakeBroadcastAddress.Trim()
        };
    }

    private StorageConfigurationEdit FromBootstrapConfiguration(IConfiguration source)
    {
        var provider = string.Equals(source["Storage:Provider"], "smb", StringComparison.OrdinalIgnoreCase) ? StorageProviderType.Smb : StorageProviderType.Local;
        SmbEndpoint? endpoint = null;
        if (provider == StorageProviderType.Smb && !string.IsNullOrWhiteSpace(source["Storage:SmbRootPath"]))
        {
            endpoint = SmbEndpoint.Parse(source["Storage:SmbRootPath"]!);
        }

        var wakePolicy = Enum.TryParse<WakePolicy>(source["Storage:WakePolicy"], true, out var parsedWakePolicy)
            ? parsedWakePolicy
            : WakePolicy.Never;
        return new StorageConfigurationEdit(
            provider,
            source["Storage:RootPath"] ?? "/data/documents",
            endpoint?.Server,
            endpoint?.Share,
            endpoint?.BasePath,
            source["Storage:SmbUsername"],
            source["Storage:SmbPassword"],
            source["Storage:SmbDomain"],
            wakePolicy,
            source["Storage:WakeMacAddress"],
            source["Storage:WakeBroadcastAddress"] ?? "255.255.255.255");
    }

    private StorageConfiguration CreateEntity(StorageConfigurationEdit edit, string? existingEncryptedPassword) => new()
    {
        Id = ActiveConfigurationId,
        ProviderType = edit.ProviderType,
        LocalRootPath = edit.LocalRootPath.Trim(),
        SmbServer = NormalizeOptional(edit.SmbServer),
        SmbShare = NormalizeOptional(edit.SmbShare),
        SmbBasePath = NormalizeOptional(edit.SmbBasePath),
        SmbUsername = NormalizeOptional(edit.SmbUsername),
        EncryptedSmbPassword = string.IsNullOrWhiteSpace(edit.SmbPassword) ? existingEncryptedPassword : passwordProtector.Protect(edit.SmbPassword),
        SmbDomain = NormalizeOptional(edit.SmbDomain),
        WakePolicy = edit.WakePolicy,
        WakeMacAddress = NormalizeOptional(edit.WakeMacAddress),
        WakeBroadcastAddress = edit.WakeBroadcastAddress.Trim(),
        UpdatedAt = timeProvider.GetUtcNow().UtcDateTime
    };

    private StorageOptions ToOptions(StorageConfiguration entity) => new()
    {
        Provider = entity.ProviderType == StorageProviderType.Smb ? "smb" : "local",
        RootPath = entity.LocalRootPath,
        SmbRootPath = entity.SmbServer is null || entity.SmbShare is null ? null : SmbEndpoint.Create(entity.SmbServer, entity.SmbShare, entity.SmbBasePath).ToRootPath(),
        SmbUsername = entity.SmbUsername,
        SmbPassword = string.IsNullOrWhiteSpace(entity.EncryptedSmbPassword) ? null : passwordProtector.Unprotect(entity.EncryptedSmbPassword),
        SmbDomain = entity.SmbDomain,
        WakePolicy = entity.WakePolicy,
        WakeMacAddress = entity.WakeMacAddress,
        WakeBroadcastAddress = entity.WakeBroadcastAddress
    };

    private static StorageConfigurationView ToView(StorageConfiguration entity) => new(
        entity.ProviderType,
        entity.LocalRootPath,
        entity.SmbServer,
        entity.SmbShare,
        entity.SmbBasePath,
        entity.SmbUsername,
        entity.SmbDomain,
        !string.IsNullOrWhiteSpace(entity.EncryptedSmbPassword),
        entity.WakePolicy,
        entity.WakeMacAddress,
        entity.WakeBroadcastAddress,
        entity.UpdatedAt);

    private static bool MatchesStorageLocation(StorageConfiguration entity, StorageConfigurationEdit edit) =>
        entity.ProviderType == edit.ProviderType &&
        string.Equals(entity.LocalRootPath, edit.LocalRootPath.Trim(), StringComparison.Ordinal) &&
        string.Equals(entity.SmbServer, NormalizeOptional(edit.SmbServer), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entity.SmbShare, NormalizeOptional(edit.SmbShare), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entity.SmbBasePath, NormalizeOptional(edit.SmbBasePath), StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class StorageConfigurationValidator
{
    public static string? Validate(StorageConfigurationEdit edit)
    {
        if (!Enum.IsDefined(edit.ProviderType))
        {
            return "Der Speichertyp ist ungültig.";
        }

        if (edit.ProviderType == StorageProviderType.Local)
        {
            if (string.IsNullOrWhiteSpace(edit.LocalRootPath))
            {
                return "Der lokale Speicherpfad ist erforderlich.";
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(edit.SmbServer) || string.IsNullOrWhiteSpace(edit.SmbShare))
            {
                return "Server und Freigabe sind für SMB erforderlich.";
            }

            try
            {
                SmbEndpoint.Create(edit.SmbServer, edit.SmbShare, edit.SmbBasePath);
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message;
            }

            if (string.IsNullOrWhiteSpace(edit.SmbUsername))
            {
                return "Der SMB-Benutzer ist erforderlich.";
            }
        }

        if (!Enum.IsDefined(edit.WakePolicy))
        {
            return "Die Wake-on-LAN-Richtlinie ist ungültig.";
        }

        if (!System.Net.IPAddress.TryParse(edit.WakeBroadcastAddress, out _))
        {
            return "Die Broadcast-Adresse ist ungültig.";
        }

        if (edit.WakePolicy == WakePolicy.OnDemand && !TryValidateMac(edit.WakeMacAddress))
        {
            return "Für On Demand ist eine gültige MAC-Adresse erforderlich.";
        }

        return null;
    }

    private static bool TryValidateMac(string? value)
    {
        var normalized = value?.Replace(":", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        return normalized?.Length == 12 && normalized.All(Uri.IsHexDigit);
    }
}
