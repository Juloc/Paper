using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Auth;

namespace Paper.Web.Pages.Account;

public sealed class LogoutModel : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        await OwnerAuthService.SignOutAsync(HttpContext);
        return RedirectToPage("/Account/Login");
    }
}
