using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Shelf;

namespace Paper.Web.Pages.Shelf;

public sealed class IndexModel(ShelfFolderStore folders, DocumentFilingService filing) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long? FolderId { get; set; }

    [BindProperty(SupportsGet = true)]
    public ShelfDocumentSort Sort { get; set; } = ShelfDocumentSort.DateDescending;

    [BindProperty]
    public string NewFolderName { get; set; } = "";

    public IReadOnlyList<ShelfFolderOption> Folders { get; private set; } = [];
    public IReadOnlyList<ShelfFolderTreeNode> FolderTree { get; private set; } = [];
    public ShelfFolderView? CurrentFolder { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(Sort))
        {
            Sort = ShelfDocumentSort.DateDescending;
        }

        Folders = await folders.ListOptionsAsync(cancellationToken);
        FolderTree = BuildTree(Folders, FolderId);
        if (FolderId is not null)
        {
            CurrentFolder = await folders.GetAsync(FolderId.Value, Sort, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostCreateAsync(long? parentId, CancellationToken cancellationToken)
    {
        var folder = await folders.CreateAsync(parentId, NewFolderName, cancellationToken);
        if (folder is null)
        {
            ModelState.AddModelError(nameof(NewFolderName), "Der Ordnername ist ungültig oder existiert bereits.");
            FolderId = parentId;
            await OnGetAsync(cancellationToken);
            return Page();
        }

        TempData["Status"] = "Regalordner erstellt.";
        return RedirectToPage(new { folderId = folder.Id, sort = Sort });
    }

    public async Task<IActionResult> OnPostUpdateAsync(long id, long? parentId, string name, CancellationToken cancellationToken)
    {
        var result = await folders.UpdateLocationAsync(id, parentId, name, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(NewFolderName), result.Error ?? "Der Ordner konnte nicht geändert werden.");
            FolderId = id;
            await OnGetAsync(cancellationToken);
            return Page();
        }

        TempData["Status"] = "Regalordner geändert.";
        return RedirectToPage(new { folderId = id, sort = Sort });
    }

    public async Task<IActionResult> OnPostMoveDocumentAsync(long documentId, long shelfFolderId, CancellationToken cancellationToken)
    {
        var result = await filing.MoveToShelfAsync(documentId, shelfFolderId, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            TempData["Status"] = result.Error ?? "Das Dokument konnte nicht verschoben werden.";
            return RedirectToPage(new { folderId = FolderId, sort = Sort });
        }

        TempData["Status"] = "Dokument verschoben.";
        return RedirectToPage(new { folderId = shelfFolderId, sort = Sort });
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id, CancellationToken cancellationToken)
    {
        var result = await folders.DeleteAsync(id, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            TempData["Status"] = result.Error ?? "Der Ordner konnte nicht gelöscht werden.";
            return RedirectToPage(new { folderId = id, sort = Sort });
        }

        TempData["Status"] = "Regalordner gelöscht.";
        return RedirectToPage(new { sort = Sort });
    }

    private static IReadOnlyList<ShelfFolderTreeNode> BuildTree(
        IReadOnlyList<ShelfFolderOption> options,
        long? selectedId)
    {
        var childrenByParent = options
            .GroupBy(option => option.ParentId)
            .ToLookup(group => group.Key, group => group.OrderBy(option => option.Name).ToArray());

        ShelfFolderTreeNode Build(ShelfFolderOption option)
        {
            var childOptions = childrenByParent[option.Id].FirstOrDefault();
            var children = childOptions is not null
                ? childOptions.Select(Build).ToArray()
                : [];
            return new ShelfFolderTreeNode(option, children, selectedId == option.Id || children.Any(child => child.IsExpanded), selectedId == option.Id);
        }

        var rootOptions = childrenByParent[null].FirstOrDefault();
        return rootOptions is not null
            ? rootOptions.Select(Build).ToArray()
            : [];
    }
}
