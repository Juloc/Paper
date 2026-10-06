using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Processing;

namespace Paper.Web.Pages.Documents;

public sealed class DetailModel(DocumentStore documents) : PageModel
{
    [BindProperty]
    public EditDocumentInput Input { get; set; } = new();

    public DocumentDetails? Document { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        Document = await documents.GetAsync(id, cancellationToken);
        return Document is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(long id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !await documents.UpdateAsync(id, Input.Title, Input.DocumentDate, Input.Tags, cancellationToken))
        {
            Document = await documents.GetAsync(id, cancellationToken);
            return Document is null ? NotFound() : Page();
        }

        TempData["Status"] = "Dokument gespeichert.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostArchiveAsync(long id, CancellationToken cancellationToken)
    {
        if (!await documents.ArchiveAsync(id, cancellationToken))
        {
            return NotFound();
        }

        return RedirectToPage("/Documents/Index");
    }
}

public sealed class EditDocumentInput
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(300)]
    public string Title { get; set; } = "";

    public DateOnly? DocumentDate { get; set; }

    public string Tags { get; set; } = "";
}
