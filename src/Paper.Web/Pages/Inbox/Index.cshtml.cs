using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Documents;

namespace Paper.Web.Pages.Inbox;

public sealed class IndexModel(DocumentStore documents) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public DocumentStatus View { get; set; } = DocumentStatus.Inbox;

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public long? SelectedId { get; set; }

    public IReadOnlyList<DocumentListItem> Documents { get; private set; } = [];
    public int DeferredCount { get; private set; }
    public int IgnoredCount { get; private set; }
    public int CurrentCount { get; private set; }
    public int PageCount { get; private set; }
    public DocumentListItem? SelectedDocument { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (View is not (DocumentStatus.Inbox or DocumentStatus.Deferred or DocumentStatus.Ignored))
        {
            View = DocumentStatus.Inbox;
        }

        var page = await documents.ListPageAsync(View, PageNumber, cancellationToken);
        Documents = page.Documents;
        PageNumber = page.PageNumber;
        PageCount = page.PageCount;
        CurrentCount = page.TotalCount;
        SelectedDocument = Documents.FirstOrDefault(document => document.Id == SelectedId) ?? Documents.FirstOrDefault();
        SelectedId = SelectedDocument?.Id;
        DeferredCount = await documents.CountAsync(DocumentStatus.Deferred, cancellationToken);
        IgnoredCount = await documents.CountAsync(DocumentStatus.Ignored, cancellationToken);
    }

    public Task<IActionResult> OnPostDeferAsync(long id, CancellationToken cancellationToken) =>
        SetStatusAsync(id, DocumentStatus.Deferred, "Dokument für später zurückgestellt.", cancellationToken);

    public Task<IActionResult> OnPostIgnoreAsync(long id, CancellationToken cancellationToken) =>
        SetStatusAsync(id, DocumentStatus.Ignored, "Dokument ignoriert.", cancellationToken);

    public Task<IActionResult> OnPostRestoreAsync(long id, CancellationToken cancellationToken) =>
        SetStatusAsync(id, DocumentStatus.Inbox, "Dokument wieder in die Inbox gelegt.", cancellationToken);

    public async Task<IActionResult> OnPostReanalyzeAsync(long id, CancellationToken cancellationToken)
    {
        if (!await documents.QueueReanalysisAsync(id, cancellationToken))
        {
            return NotFound();
        }

        TempData["Status"] = "Dokument wird erneut analysiert.";
        return RedirectToPage(new { view = View, pageNumber = PageNumber, selectedId = id });
    }

    private async Task<IActionResult> SetStatusAsync(long id, DocumentStatus status, string message, CancellationToken cancellationToken)
    {
        if (!await documents.SetInboxStatusAsync(id, status, cancellationToken))
        {
            return NotFound();
        }

        TempData["Status"] = message;
        return RedirectToPage(new { view = View, pageNumber = PageNumber, selectedId = id });
    }

}
