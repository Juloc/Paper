using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Correspondents;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.DocumentTypes;
using Paper.Web.Features.Tags;

namespace Paper.Web.Pages.Settings.Metadata;

public sealed class IndexModel(
    CorrespondentStore correspondents,
    DocumentTypeStore documentTypes,
    CustomFieldStore customFields,
    TagStore tags) : PageModel
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

    public IReadOnlyList<CorrespondentOption> CorrespondentOptions { get; private set; } = [];
    public IReadOnlyList<DocumentTypeOption> DocumentTypeOptions { get; private set; } = [];
    public IReadOnlyList<CustomFieldOption> CustomFieldOptions { get; private set; } = [];
    public IReadOnlyList<TagOption> TagOptions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

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

    public async Task<IActionResult> OnPostDeleteCorrespondentAsync(long id, CancellationToken cancellationToken)
    {
        var result = await correspondents.DeleteAsync(id, cancellationToken);
        TempData["Status"] = result switch
        {
            { Deleted: true } => "Korrespondent entfernt.",
            { InUse: true } => "Der Korrespondent wird noch verwendet.",
            _ => "Der Korrespondent wurde nicht gefunden."
        };
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

    public async Task<IActionResult> OnPostDeleteDocumentTypeAsync(long id, CancellationToken cancellationToken)
    {
        var result = await documentTypes.DeleteAsync(id, cancellationToken);
        TempData["Status"] = result switch
        {
            { Deleted: true } => "Dokumenttyp entfernt.",
            { InUse: true } => "Der Dokumenttyp wird noch verwendet.",
            _ => "Der Dokumenttyp wurde nicht gefunden."
        };
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

    public async Task<IActionResult> OnPostDeleteCustomFieldAsync(long id, CancellationToken cancellationToken)
    {
        var result = await customFields.DeleteAsync(id, cancellationToken);
        TempData["Status"] = result switch
        {
            { Deleted: true } => "Zusatzfeld entfernt.",
            { InUse: true } => "Das Zusatzfeld wird noch verwendet.",
            _ => "Das Zusatzfeld wurde nicht gefunden."
        };
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

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CorrespondentOptions = await correspondents.ListAsync(cancellationToken);
        DocumentTypeOptions = await documentTypes.ListAsync(cancellationToken);
        CustomFieldOptions = await customFields.ListAsync(cancellationToken);
        TagOptions = await tags.ListAsync(cancellationToken);
    }
}
