using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Documents;

namespace Paper.Web.Pages.Documents;

public sealed class IndexModel(DocumentStore documents) : PageModel
{
    public IReadOnlyList<DocumentListItem> Documents { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Documents = await documents.ListFiledAsync(cancellationToken);
}
