using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Correspondents;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.DocumentTypes;

namespace Paper.Web.Pages.Settings;

public sealed class IndexModel(
    IConfiguration configuration,
    CorrespondentStore correspondents,
    DocumentTypeStore documentTypes,
    CustomFieldStore customFields) : PageModel
{
    [BindProperty]
    public string CorrespondentName { get; set; } = "";

    [BindProperty]
    public string DocumentTypeName { get; set; } = "";

    [BindProperty]
    public string CustomFieldName { get; set; } = "";

    [BindProperty]
    public CustomFieldType CustomFieldType { get; set; } = CustomFieldType.Text;

    public string StoragePath => configuration["Storage:RootPath"] ?? "/data/documents";
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

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CorrespondentOptions = await correspondents.ListAsync(cancellationToken);
        DocumentTypeOptions = await documentTypes.ListAsync(cancellationToken);
        CustomFieldOptions = await customFields.ListAsync(cancellationToken);
    }
}
