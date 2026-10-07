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
using Paper.Web.Features.Storage;
using Paper.Web.Features.Tags;

namespace Paper.Web.Pages.Settings;

public sealed class IndexModel(
    IConfiguration configuration,
    CorrespondentStore correspondents,
    DocumentTypeStore documentTypes,
    CustomFieldStore customFields,
    TagStore tags,
    ConsumeFailureStore consumeFailures,
    DocumentRestoreService restore,
    PaperlessImportService paperlessImport,
    ImapMailImportWorker mailImport,
    AnalysisRuleStore analysisRules,
    StorageIntegrityService storageIntegrity,
    AppDbContext db) : PageModel
{
    [BindProperty]
    public string CorrespondentName { get; set; } = "";

    [BindProperty]
    public string DocumentTypeName { get; set; } = "";

    [BindProperty]
    public string CustomFieldName { get; set; } = "";

    [BindProperty]
    public string TagName { get; set; } = "";

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
    public bool MailEnabled
    {
        get
        {
            try
            {
                return MailConfiguration.Load(configuration).Any(account => account.Enabled);
            }
            catch (InvalidOperationException exception)
            {
                MailConfigurationError = exception.Message;
                return false;
            }
        }
    }
    public string? MailConfigurationError { get; private set; }
    public IReadOnlyList<CorrespondentOption> CorrespondentOptions { get; private set; } = [];
    public IReadOnlyList<DocumentTypeOption> DocumentTypeOptions { get; private set; } = [];
    public IReadOnlyList<CustomFieldOption> CustomFieldOptions { get; private set; } = [];
    public IReadOnlyList<TagOption> TagOptions { get; private set; } = [];
    public IReadOnlyList<AnalysisRuleView> AnalysisRules { get; private set; } = [];
    public IReadOnlyList<MailImportState> MailStates { get; private set; } = [];
    public IReadOnlyList<MailImportFailure> MailFailures { get; private set; } = [];
    public IReadOnlyList<ConsumeFailureView> ConsumeFailures { get; private set; } = [];
    public StorageIntegrityReport? IntegrityReport { get; private set; }

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

    public async Task<IActionResult> OnPostCreateTagAsync(CancellationToken cancellationToken)
    {
        if (await tags.CreateAsync(TagName, cancellationToken) is null)
        {
            ModelState.AddModelError(nameof(TagName), "Der Tag ist ungültig.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["Status"] = "Tag gespeichert.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteTagAsync(long id, CancellationToken cancellationToken)
    {
        if (await tags.DeleteAsync(id, cancellationToken))
        {
            TempData["Status"] = "Tag entfernt.";
        }

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

    public async Task<IActionResult> OnPostCheckStorageAsync(CancellationToken cancellationToken)
    {
        try
        {
            IntegrityReport = await storageIntegrity.CheckAsync(cancellationToken);
            await LoadAsync(cancellationToken);
            return Page();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ModelState.AddModelError(string.Empty, $"Die Speicherprüfung konnte nicht ausgeführt werden: {exception.Message}");
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CorrespondentOptions = await correspondents.ListAsync(cancellationToken);
        DocumentTypeOptions = await documentTypes.ListAsync(cancellationToken);
        CustomFieldOptions = await customFields.ListAsync(cancellationToken);
        TagOptions = await tags.ListAsync(cancellationToken);
        AnalysisRules = await analysisRules.ListAsync(cancellationToken);
        MailStates = await db.MailImportStates.AsNoTracking()
            .OrderBy(state => state.AccountName)
            .ToListAsync(cancellationToken);
        MailFailures = await db.MailImportFailures.AsNoTracking()
            .OrderByDescending(failure => failure.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);
        ConsumeFailures = await consumeFailures.ListAsync(cancellationToken);
    }
}
