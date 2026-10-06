using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Documents;

namespace Paper.Web.Pages.Add;

public sealed class IndexModel(DocumentImportService importer) : PageModel
{
    [BindProperty]
    public IFormFile? Upload { get; set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Upload is null)
        {
            ModelState.AddModelError(nameof(Upload), "Bitte wähle eine Datei aus.");
            return Page();
        }

        var result = await importer.ImportAsync(Upload, cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(nameof(Upload), result.Error ?? "Der Upload ist fehlgeschlagen.");
            return Page();
        }

        TempData["Status"] = "Dokument hinzugefügt. Die Verarbeitung läuft im Hintergrund.";
        return RedirectToPage("/Inbox/Index");
    }
}
