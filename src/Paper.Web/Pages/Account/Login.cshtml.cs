using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Auth;

namespace Paper.Web.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel(OwnerAuthService auth, LoginAttemptLimiter limiter, TimeProvider timeProvider) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        var clientKey = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!limiter.TryBegin(clientKey, timeProvider.GetUtcNow(), out _))
        {
            ModelState.AddModelError(string.Empty, "Zu viele fehlgeschlagene Anmeldeversuche. Bitte später erneut versuchen.");
            return Page();
        }

        if (!ModelState.IsValid || !auth.Validate(Input.Username, Input.Password))
        {
            limiter.RecordFailure(clientKey, timeProvider.GetUtcNow());
            ModelState.AddModelError(string.Empty, "Benutzername oder Passwort ist nicht korrekt.");
            return Page();
        }

        limiter.RecordSuccess(clientKey);
        var principal = OwnerAuthService.CreatePrincipal(Input.Username);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = true });
        return LocalRedirect(returnUrl is not null && Url.IsLocalUrl(returnUrl) ? returnUrl : "/Inbox");
    }
}

public sealed class LoginInput
{
    [Required, StringLength(100)]
    public string Username { get; set; } = "";

    [Required, StringLength(200)]
    public string Password { get; set; } = "";
}
