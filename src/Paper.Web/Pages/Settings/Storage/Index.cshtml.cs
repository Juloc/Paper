using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Storage;

namespace Paper.Web.Pages.Settings.Storage;

public sealed class IndexModel(
    StorageConfigurationStore configurations,
    StorageConnectionTestService connectionTests,
    StorageIntegrityService storageIntegrity) : PageModel
{
    [BindProperty]
    public StorageProviderType ProviderType { get; set; }

    [BindProperty]
    public string LocalRootPath { get; set; } = string.Empty;

    [BindProperty]
    public string? SmbServer { get; set; }

    [BindProperty]
    public string? SmbShare { get; set; }

    [BindProperty]
    public string? SmbBasePath { get; set; }

    [BindProperty]
    public string? SmbUsername { get; set; }

    [BindProperty]
    public string? SmbPassword { get; set; }

    [BindProperty]
    public string? SmbDomain { get; set; }

    [BindProperty]
    public WakePolicy WakePolicy { get; set; }

    [BindProperty]
    public string? WakeMacAddress { get; set; }

    [BindProperty]
    public string WakeBroadcastAddress { get; set; } = "255.255.255.255";

    public StorageConfigurationView Current { get; private set; } = null!;

    public StorageConnectionTestResult? ConnectionTest { get; private set; }

    public StorageIntegrityReport? IntegrityReport { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var result = await configurations.SaveAsync(CreateEdit(), cancellationToken);
        if (!result.Saved)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Die Speicher-Konfiguration konnte nicht gespeichert werden.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["Status"] = "Die Speicher-Konfiguration wurde gespeichert.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(CancellationToken cancellationToken)
    {
        ConnectionTest = await connectionTests.TestAsync(CreateEdit(), cancellationToken);
        await LoadCurrentAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCheckStorageAsync(CancellationToken cancellationToken)
    {
        IntegrityReport = await storageIntegrity.CheckAsync(cancellationToken);
        await LoadCurrentAsync(cancellationToken);
        return Page();
    }

    private StorageConfigurationEdit CreateEdit() => new(
        ProviderType,
        LocalRootPath,
        SmbServer,
        SmbShare,
        SmbBasePath,
        SmbUsername,
        SmbPassword,
        SmbDomain,
        WakePolicy,
        WakeMacAddress,
        WakeBroadcastAddress);

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await LoadCurrentAsync(cancellationToken);
        ProviderType = Current.ProviderType;
        LocalRootPath = Current.LocalRootPath;
        SmbServer = Current.SmbServer;
        SmbShare = Current.SmbShare;
        SmbBasePath = Current.SmbBasePath;
        SmbUsername = Current.SmbUsername;
        SmbDomain = Current.SmbDomain;
        WakePolicy = Current.WakePolicy;
        WakeMacAddress = Current.WakeMacAddress;
        WakeBroadcastAddress = Current.WakeBroadcastAddress;
    }

    private async Task LoadCurrentAsync(CancellationToken cancellationToken)
    {
        Current = await configurations.GetAsync(cancellationToken);
    }
}
