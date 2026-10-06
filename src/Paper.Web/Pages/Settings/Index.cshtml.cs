using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Correspondents;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.DocumentTypes;
using Paper.Web.Features.Export;
using Paper.Web.Features.Import;

namespace Paper.Web.Pages.Settings;

public sealed class IndexModel(
    IConfiguration configuration,
    CorrespondentStore correspondents,
    DocumentTypeStore documentTypes,
    CustomFieldStore customFields,
    DocumentRestoreService restore,
    PaperlessImportService paperlessImport) : PageModel
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

    public string StoragePath => configuration["Storage:RootPath"] ?? "/data/documents";
    public string StorageProvider => configuration["Storage:Provider"] ?? "local";
    public string WakePolicy => configuration["Storage:WakePolicy"] ?? "Never";
    public string OcrLanguage => configuration["Ocr:Language"] ?? "eng";
    public IReadOnlyList<CorrespondentOption> CorrespondentOptions { get; private set; } = [];
    public IReadOnlyList<DocumentTypeOption> DocumentTypeOptions { get; private set; } = [];
    public IReadOnlyList<CustomFieldOption> CustomFieldOptions { get; private set; } = [];

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

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CorrespondentOptions = await correspondents.ListAsync(cancellationToken);
        DocumentTypeOptions = await documentTypes.ListAsync(cancellationToken);
        CustomFieldOptions = await customFields.ListAsync(cancellationToken);
    }
}
