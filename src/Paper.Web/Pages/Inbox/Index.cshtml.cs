using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Documents;

namespace Paper.Web.Pages.Inbox;

public sealed class IndexModel(DocumentStore documents) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public DocumentStatus View { get; set; } = DocumentStatus.Inbox;

    public IReadOnlyList<DocumentListItem> Documents { get; private set; } = [];
    public int DeferredCount { get; private set; }
    public int IgnoredCount { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (View is not (DocumentStatus.Inbox or DocumentStatus.Deferred or DocumentStatus.Ignored))
        {
            View = DocumentStatus.Inbox;
        }

        Documents = await documents.ListAsync(View, cancellationToken);
        DeferredCount = await documents.CountAsync(DocumentStatus.Deferred, cancellationToken);
        IgnoredCount = await documents.CountAsync(DocumentStatus.Ignored, cancellationToken);
    }

    public Task<IActionResult> OnPostDeferAsync(long id, CancellationToken cancellationToken) =>
        SetStatusAsync(id, DocumentStatus.Deferred, "Dokument für später zurückgestellt.", cancellationToken);

    public Task<IActionResult> OnPostIgnoreAsync(long id, CancellationToken cancellationToken) =>
        SetStatusAsync(id, DocumentStatus.Ignored, "Dokument ignoriert.", cancellationToken);

    public Task<IActionResult> OnPostRestoreAsync(long id, CancellationToken cancellationToken) =>
        SetStatusAsync(id, DocumentStatus.Inbox, "Dokument wieder in die Inbox gelegt.", cancellationToken);

    private async Task<IActionResult> SetStatusAsync(long id, DocumentStatus status, string message, CancellationToken cancellationToken)
    {
        if (!await documents.SetInboxStatusAsync(id, status, cancellationToken))
        {
            return NotFound();
        }

        TempData["Status"] = message;
        return RedirectToPage(new { view = View });
    }

}
