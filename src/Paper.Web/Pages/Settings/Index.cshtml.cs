using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Correspondents;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.DocumentTypes;
using Paper.Web.Features.Export;
using Paper.Web.Features.Import;
using Paper.Web.Features.Processing;

namespace Paper.Web.Pages.Settings;

public sealed class IndexModel(
    IConfiguration configuration,
    CorrespondentStore correspondents,
    DocumentTypeStore documentTypes,
    CustomFieldStore customFields,
    DocumentRestoreService restore,
    PaperlessImportService paperlessImport,
    ImapMailImportWorker mailImport,
    AnalysisRuleStore analysisRules,
    AppDbContext db) : PageModel
{
    [BindProperty]
    public string CorrespondentName { get; set; } = "";

    [BindProperty]
    public string DocumentTypeName { get; set; } = "";

    [BindProperty]
    public string CustomFieldName { get; set; } = "";

    [BindProperty]
    public CustomFieldType CustomFieldType { get; set; } = CustomFieldType.Text;

    [BindProperty]
    public IFormFile? Backup { get; set; }

    [BindProperty]
    public IFormFile? PaperlessExport { get; set; }

    public string StorageProvider => configuration["Storage:Provider"] ?? "local";
    public string StoragePath => StorageProvider.Equals("smb", StringComparison.OrdinalIgnoreCase)
        ? configuration["Storage:SmbRootPath"] ?? "(nicht konfiguriert)"
        : configuration["Storage:RootPath"] ?? "/data/documents";
    public string WakePolicy => configuration["Storage:WakePolicy"] ?? "Never";
    public string OcrLanguage => configuration["Ocr:Language"] ?? "eng";
    public bool MailEnabled => configuration.GetValue<bool>("Mail:Enabled");
    public IReadOnlyList<CorrespondentOption> CorrespondentOptions { get; private set; } = [];
    public IReadOnlyList<DocumentTypeOption> DocumentTypeOptions { get; private set; } = [];
    public IReadOnlyList<CustomFieldOption> CustomFieldOptions { get; private set; } = [];
    public IReadOnlyList<AnalysisRuleView> AnalysisRules { get; private set; } = [];
    public MailImportState? MailState { get; private set; }
    public IReadOnlyList<MailImportFailure> MailFailures { get; private set; } = [];

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateCorrespondentAsync(CancellationToken cancellationToken)
    {
        if (await correspondents.CreateAsync(CorrespondentName, cancellationToken) is null)
        {
            ModelState.AddModelError(nameof(CorrespondentName), "Der Name ist ungültig.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["Status"] = "Korrespondent gespeichert.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateDocumentTypeAsync(CancellationToken cancellationToken)
    {
        if (await documentTypes.CreateAsync(DocumentTypeName, cancellationToken) is null)
        {
            ModelState.AddModelError(nameof(DocumentTypeName), "Der Name ist ungültig.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["Status"] = "Dokumenttyp gespeichert.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateCustomFieldAsync(CancellationToken cancellationToken)
    {
        if (await customFields.CreateAsync(CustomFieldName, CustomFieldType, cancellationToken) is null)
        {
            ModelState.AddModelError(nameof(CustomFieldName), "Name oder Feldtyp ist ungültig.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["Status"] = "Zusatzfeld gespeichert.";
        return RedirectToPage();
    }

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
            TempData["Status"] = result.Executed
                ? $"Mailimport ausgeführt: {result.Processed} Nachricht(en), {result.Imported} Anhang/Anhänge importiert."
                : "Der Mailimport ist deaktiviert.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAnalysisRuleAsync(long id, CancellationToken cancellationToken)
    {
        if (await analysisRules.DeleteAsync(id, cancellationToken))
        {
            TempData["Status"] = "Lernregel entfernt.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CorrespondentOptions = await correspondents.ListAsync(cancellationToken);
        DocumentTypeOptions = await documentTypes.ListAsync(cancellationToken);
        CustomFieldOptions = await customFields.ListAsync(cancellationToken);
        AnalysisRules = await analysisRules.ListAsync(cancellationToken);
        MailState = await db.MailImportStates.AsNoTracking()
            .OrderByDescending(state => state.LastSyncAt)
            .FirstOrDefaultAsync(cancellationToken);
        MailFailures = await db.MailImportFailures.AsNoTracking()
            .OrderByDescending(failure => failure.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);
    }
}
