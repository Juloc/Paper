using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Processing;

namespace Paper.Web.Pages.Processing;

public sealed class IndexModel(ProcessingStatusStore processing) : PageModel
{
    public ProcessingSummary Summary { get; private set; } = new(0, 0, 0);
    public IReadOnlyList<ProcessingJobView> Jobs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Summary = await processing.GetSummaryAsync(cancellationToken);
        Jobs = await processing.ListRecentAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRetryAsync(long id, CancellationToken cancellationToken)
    {
        if (!await processing.RetryAsync(id, cancellationToken))
        {
            return NotFound();
        }

        TempData["Status"] = "Verarbeitung erneut eingeplant.";
        return RedirectToPage();
    }
}
