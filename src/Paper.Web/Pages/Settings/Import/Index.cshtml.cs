using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Export;
using Paper.Web.Features.Import;

namespace Paper.Web.Pages.Settings.Import;

public sealed class IndexModel(
    IConfiguration configuration,
    ConsumeFailureStore consumeFailures,
    MailImportStatusStore mailStatus,
    DocumentRestoreService restore,
    PaperlessImportService paperlessImport,
    ImapMailImportWorker mailImport) : PageModel
{
    [BindProperty]
    public IFormFile? Backup { get; set; }

    [BindProperty]
    public IFormFile? PaperlessExport { get; set; }

    public bool MailEnabled { get; private set; }
    public string? MailConfigurationError { get; private set; }
    public IReadOnlyList<MailImportStateView> MailStates { get; private set; } = [];
    public IReadOnlyList<MailImportFailureView> MailFailures { get; private set; } = [];
    public IReadOnlyList<ConsumeFailureView> ConsumeFailures { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostRestoreAsync(CancellationToken cancellationToken)
    {
        if (Backup is null)
        {
            ModelState.AddModelError(nameof(Backup), "Bitte wähle ein ZIP-Backup aus.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        await using var stream = Backup.OpenReadStream();
        var result = await restore.RestoreAsync(stream, Backup.Length, cancellationToken);
        if (result.Errors.Count > 0)
        {
            ModelState.AddModelError(nameof(Backup), $"Import mit {result.Errors.Count} Fehler(n): {string.Join(" | ", result.Errors.Take(3))}");
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["Status"] = $"Backup importiert: {result.Imported} Dokument(e), {result.Skipped} Duplikat(e) übersprungen.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportPaperlessAsync(CancellationToken cancellationToken)
    {
        if (PaperlessExport is null)
        {
            ModelState.AddModelError(nameof(PaperlessExport), "Bitte wähle einen Paperless-Export als ZIP aus.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        await using var stream = PaperlessExport.OpenReadStream();
        var result = await paperlessImport.ImportAsync(stream, PaperlessExport.Length, cancellationToken);
        if (result.Errors.Count > 0)
        {
            ModelState.AddModelError(nameof(PaperlessExport), $"Import mit {result.Errors.Count} Fehler(n): {string.Join(" | ", result.Errors.Take(3))}");
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["Status"] = $"Paperless-Export importiert: {result.Imported} Dokument(e), {result.Skipped} Duplikat(e) übersprungen.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRunMailImportAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await mailImport.RunOnceAsync(cancellationToken);
            TempData["Status"] = result.Executed ? $"Mailimport ausgeführt: {result.Processed} Nachricht(en), {result.Imported} Anhang/Anhänge importiert." : "Der Mailimport ist deaktiviert.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            MailEnabled = MailConfiguration.Load(configuration).Any(account => account.Enabled);
        }
        catch (InvalidOperationException exception)
        {
            MailConfigurationError = exception.Message;
        }

        MailStates = await mailStatus.ListStatesAsync(cancellationToken);
        MailFailures = await mailStatus.ListFailuresAsync(cancellationToken);
        ConsumeFailures = await consumeFailures.ListAsync(cancellationToken);
    }
}
