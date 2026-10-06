using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Documents;

namespace Paper.Web.Pages.Add;

public sealed class IndexModel(DocumentImportService importer) : PageModel
{
    [BindProperty]
    public IReadOnlyList<IFormFile> Uploads { get; set; } = [];

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Uploads.Count == 0)
        {
            ModelState.AddModelError(nameof(Uploads), "Bitte wähle mindestens eine Datei aus.");
            return Page();
        }

        var imported = 0;
        var errors = new List<string>();
        foreach (var upload in Uploads)
        {
            var result = await importer.ImportAsync(upload, cancellationToken);
            if (result.Success)
            {
                imported++;
            }
            else
            {
                errors.Add($"{upload.FileName}: {result.Error ?? "Upload fehlgeschlagen."}");
            }
        }

        if (imported == 0)
        {
            ModelState.AddModelError(nameof(Uploads), string.Join(" | ", errors));
            return Page();
        }

        TempData["Status"] = errors.Count == 0
            ? $"{imported} Dokument(e) hinzugefügt. Die Verarbeitung läuft im Hintergrund."
            : $"{imported} Dokument(e) hinzugefügt; {errors.Count} Datei(en) konnten nicht importiert werden: {string.Join(" | ", errors)}";
        return RedirectToPage("/Inbox/Index");
    }
}
