using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Search;

namespace Paper.Web.Pages.Search;

public sealed class IndexModel(DocumentSearchService search) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Query { get; set; } = "";

    public IReadOnlyList<DocumentListItem> Results { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Results = await search.SearchAsync(Query, cancellationToken);
}
