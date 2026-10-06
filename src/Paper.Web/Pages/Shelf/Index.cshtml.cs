using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Shelf;

namespace Paper.Web.Pages.Shelf;

public sealed class IndexModel(ShelfFolderStore folders) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long? FolderId { get; set; }

    [BindProperty]
    public string NewFolderName { get; set; } = "";

    public IReadOnlyList<ShelfFolderOption> Folders { get; private set; } = [];
    public ShelfFolderView? CurrentFolder { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Folders = await folders.ListOptionsAsync(cancellationToken);
        if (FolderId is not null)
        {
            CurrentFolder = await folders.GetAsync(FolderId.Value, cancellationToken);
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
        return RedirectToPage(new { folderId = folder.Id });
    }
}
