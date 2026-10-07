using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Correspondents;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.DocumentTypes;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Search;
using Paper.Web.Features.Shelf;
using Paper.Web.Features.Tags;

namespace Paper.Web.Pages.Search;

public sealed class IndexModel(
    DocumentSearchService search,
    CorrespondentStore correspondents,
    DocumentTypeStore documentTypes,
    ShelfFolderStore shelfFolders,
    CustomFieldStore customFields,
    TagStore tags) : PageModel
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
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    public int PageCount { get; private set; }
    public int TotalCount { get; private set; }
    public IReadOnlyList<CorrespondentOption> Correspondents { get; private set; } = [];
    public IReadOnlyList<DocumentTypeOption> DocumentTypes { get; private set; } = [];
    public IReadOnlyList<ShelfFolderOption> ShelfFolders { get; private set; } = [];
    public IReadOnlyList<CustomFieldOption> CustomFields { get; private set; } = [];
    public IReadOnlyList<TagOption> Tags { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var criteria = new SearchCriteria(Query, CorrespondentId, DocumentTypeId, ShelfFolderId, Tag, FromDate, ToDate, CustomFieldId, CustomFieldValue);
        var page = await search.SearchAsync(criteria, PageNumber, cancellationToken);
        Results = page.Results;
        PageNumber = page.PageNumber;
        PageCount = page.PageCount;
        TotalCount = page.TotalCount;
        Correspondents = await correspondents.ListAsync(cancellationToken);
        DocumentTypes = await documentTypes.ListAsync(cancellationToken);
        ShelfFolders = await shelfFolders.ListOptionsAsync(cancellationToken);
        CustomFields = await customFields.ListAsync(cancellationToken);
        Tags = await tags.ListAsync(cancellationToken);
    }
}
