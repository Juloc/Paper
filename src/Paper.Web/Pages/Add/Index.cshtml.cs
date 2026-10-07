using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Documents;

namespace Paper.Web.Pages.Add;

[RequestSizeLimit(BatchUploadPolicy.MaximumTotalSize + 20L * 1024 * 1024)]
public sealed class IndexModel(DocumentImportService importer) : PageModel
{
    [BindProperty]
    public IReadOnlyList<IFormFile> Uploads { get; set; } = [];

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var totalSize = Uploads.Sum(upload => Math.Max(0, upload.Length));
        var batchError = BatchUploadPolicy.Validate(Uploads.Count, totalSize);
        if (batchError is not null)
        {
            ModelState.AddModelError(nameof(Uploads), batchError);
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
            ModelState.AddModelError(nameof(Uploads), BatchUploadPolicy.SummarizeErrors(errors));
            return Page();
        }

        TempData["Status"] = errors.Count == 0
            ? $"{imported} Dokument(e) hinzugefügt. Die Verarbeitung läuft im Hintergrund."
            : $"{imported} Dokument(e) hinzugefügt; {errors.Count} Datei(en) konnten nicht importiert werden: {BatchUploadPolicy.SummarizeErrors(errors)}";
        return RedirectToPage("/Inbox/Index");
    }
}
