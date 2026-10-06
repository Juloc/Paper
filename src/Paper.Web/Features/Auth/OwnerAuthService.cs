using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Paper.Web.Features.Auth;

public sealed class OwnerAuthService(IConfiguration configuration)
{
    public bool Validate(string username, string password)
    {
        var expectedUsername = configuration["Auth:Username"] ?? "";
        var expectedPassword = configuration["Auth:Password"] ?? "";
        return string.Equals(username, expectedUsername, StringComparison.Ordinal) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(expectedPassword));
    }

    public static ClaimsPrincipal CreatePrincipal(string username)
    {
        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, username));
        identity.AddClaim(new Claim(ClaimTypes.Role, "Owner"));
        return new ClaimsPrincipal(identity);
    }

    public static Task SignOutAsync(HttpContext httpContext) =>
        httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}
