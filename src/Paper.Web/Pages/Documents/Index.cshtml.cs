using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Documents;

namespace Paper.Web.Pages.Documents;

public sealed class IndexModel(DocumentStore documents) : PageModel
{
    public IReadOnlyList<DocumentListItem> Documents { get; private set; } = [];
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    public int PageCount { get; private set; }
    public int TotalCount { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var page = await documents.ListPageAsync(DocumentStatus.Filed, PageNumber, cancellationToken);
        Documents = page.Documents;
        PageNumber = page.PageNumber;
        PageCount = page.PageCount;
        TotalCount = page.TotalCount;
    }
}
