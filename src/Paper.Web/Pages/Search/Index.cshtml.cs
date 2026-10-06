using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Correspondents;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.DocumentTypes;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Search;
using Paper.Web.Features.Shelf;

namespace Paper.Web.Pages.Search;

public sealed class IndexModel(
    DocumentSearchService search,
    CorrespondentStore correspondents,
    DocumentTypeStore documentTypes,
    ShelfFolderStore shelfFolders,
    CustomFieldStore customFields) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Query { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public long? CorrespondentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? DocumentTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? ShelfFolderId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Tag { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? CustomFieldId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string CustomFieldValue { get; set; } = "";

    public IReadOnlyList<DocumentListItem> Results { get; private set; } = [];
    public IReadOnlyList<CorrespondentOption> Correspondents { get; private set; } = [];
    public IReadOnlyList<DocumentTypeOption> DocumentTypes { get; private set; } = [];
    public IReadOnlyList<ShelfFolderOption> ShelfFolders { get; private set; } = [];
    public IReadOnlyList<CustomFieldOption> CustomFields { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var criteria = new SearchCriteria(Query, CorrespondentId, DocumentTypeId, ShelfFolderId, Tag, FromDate, ToDate, CustomFieldId, CustomFieldValue);
        Results = await search.SearchAsync(criteria, cancellationToken);
        Correspondents = await correspondents.ListAsync(cancellationToken);
        DocumentTypes = await documentTypes.ListAsync(cancellationToken);
        ShelfFolders = await shelfFolders.ListOptionsAsync(cancellationToken);
        CustomFields = await customFields.ListAsync(cancellationToken);
    }
}
