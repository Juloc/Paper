using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Documents;

namespace Paper.Web.Pages.Inbox;

public sealed class IndexModel(DocumentStore documents) : PageModel
{
    public IReadOnlyList<DocumentListItem> Documents { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Documents = await documents.ListInboxAsync(cancellationToken);

    public async Task<IActionResult> OnPostArchiveAsync(long id, CancellationToken cancellationToken)
    {
        if (!await documents.ArchiveAsync(id, cancellationToken))
        {
            return NotFound();
        }

        TempData["Status"] = "Dokument archiviert.";
        return RedirectToPage();
    }
}
