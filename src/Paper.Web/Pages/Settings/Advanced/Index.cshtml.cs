using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Processing;

namespace Paper.Web.Pages.Settings.Advanced;

public sealed class IndexModel(AnalysisRuleStore analysisRules) : PageModel
{
    public IReadOnlyList<AnalysisRuleView> AnalysisRules { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => AnalysisRules = await analysisRules.ListAsync(cancellationToken);

    public async Task<IActionResult> OnPostDeleteAnalysisRuleAsync(long id, CancellationToken cancellationToken)
    {
        if (await analysisRules.DeleteAsync(id, cancellationToken))
        {
            TempData["Status"] = "Lernregel entfernt.";
        }

        return RedirectToPage();
    }
}
