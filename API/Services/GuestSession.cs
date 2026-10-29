using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using API.Database;
using API.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace API.Services;

// Guest sign-in, in its own HttpOnly, SameSite=Strict cookie and its own authentication
// scheme. It is never the default scheme, so a guest cookie never fills HttpContext.User
// on staff endpoints and can't pass for a staff session (see StaffSession.IsStaff).
// Guest endpoints ask for it by name: [Authorize(AuthenticationSchemes = GuestSession.Scheme)],
// or CurrentAsync() on endpoints open to everyone.
public static class GuestSession
{
    public const string Scheme = "Guest";
    public const string CookieName = "druid_guest";
    public const string SignInMessage = "Please sign in or create an account to book.";

    // How often a session is re-checked against the database. Changing the password
    // ends other sessions within this long.
    private static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(1);

    private const string StampClaim = "druid:stamp";
    private const string CheckedClaim = "druid:checked";

    public static Task SignInAsync(HttpContext http, GuestAccount account, string stamp) =>
        http.SignInAsync(Scheme, CreatePrincipal(account, stamp));

    public static Task SignOutAsync(HttpContext http) => http.SignOutAsync(Scheme);

    public static int AccountId(ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);

    public static GuestAccount Current(ClaimsPrincipal user) => new(
        AccountId(user),
        user.FindFirstValue(ClaimTypes.GivenName) ?? "",
        user.FindFirstValue(ClaimTypes.Surname) ?? "",
        user.FindFirstValue(ClaimTypes.Email) ?? "",
        user.FindFirstValue(ClaimTypes.MobilePhone) ?? "");

    // The signed-in guest on an endpoint that doesn't require one, or null.
    public static async Task<GuestAccount?> CurrentAsync(HttpContext http)
    {
        var result = await http.AuthenticateAsync(Scheme);
        return result.Succeeded ? Current(result.Principal) : null;
    }

    // 401 for guest endpoints. signIn tells the client this is the guest session, not staff.
    public static ObjectResult SignInRequired() =>
        new(new { message = SignInMessage, signIn = "guest" }) { StatusCode = StatusCodes.Status401Unauthorized };

    public static void Configure(CookieAuthenticationOptions options, bool isDevelopment)
    {
        options.Cookie.Name = CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // Local runs are plain http://localhost; the live site is HTTPS only.
        options.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;

        options.Events.OnRedirectToLogin = context => WriteJson(context.Response,
            StatusCodes.Status401Unauthorized, new { message = SignInMessage, signIn = "guest" });
        options.Events.OnRedirectToAccessDenied = context => WriteJson(context.Response,
            StatusCodes.Status403Forbidden, new { message = SignInMessage, signIn = "guest" });
        options.Events.OnValidatePrincipal = ValidateAsync;
    }

    private static ClaimsPrincipal CreatePrincipal(GuestAccount account, string stamp) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.GivenName, account.FirstName),
            new Claim(ClaimTypes.Surname, account.LastName),
            new Claim(ClaimTypes.Email, account.Email),
            new Claim(ClaimTypes.MobilePhone, account.Phone),
            new Claim(StampClaim, stamp),
            new Claim(CheckedClaim, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
        ], Scheme));

    private static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var user = context.Principal!;
        if (DateTime.TryParse(user.FindFirstValue(CheckedClaim), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var checkedAt) && DateTime.UtcNow - checkedAt < RecheckEvery)
            return;

        var store = context.HttpContext.RequestServices.GetRequiredService<GuestAccountStore>();
        var stamp = user.FindFirstValue(StampClaim) ?? "";
        GuestAccount? account;
        try
        {
            account = await store.GetSessionAsync(AccountId(user), stamp);
        }
        catch (Exception e) when (e is DbException or DatabaseUnavailableException)
        {
            // Keep the session; the request itself will report the database as offline.
            return;
        }

        if (account == null)
        {
            context.RejectPrincipal();
            await SignOutAsync(context.HttpContext);
            return;
        }
        context.ReplacePrincipal(CreatePrincipal(account, stamp));
        context.ShouldRenew = true;
    }

    private static Task WriteJson(HttpResponse response, int status, object body)
    {
        response.StatusCode = status;
        return response.WriteAsJsonAsync(body);
    }
}
