using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using API.Database;
using API.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace API.Services;

// Staff Portal sign-in, kept in an HttpOnly, SameSite=Strict cookie (ASP.NET Core's
// built-in cookie authentication). Every API endpoint requires a signed-in employee
// unless it's marked [AllowAnonymous] for the guest site (see Program.cs).
public static class StaffSession
{
    public const string CookieName = "druid_staff";
    public const string EndedMessage = "Your staff session has ended. Please sign in again.";

    // How often a session is re-checked against the database. Deactivating an employee
    // or changing their password ends their sessions within this long, and a role change
    // takes effect within this long.
    private static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(1);

    private const string StampClaim = "druid:stamp";
    private const string CheckedClaim = "druid:checked";

    public static Task SignInAsync(HttpContext http, Employee employee, string stamp) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, CreatePrincipal(employee, stamp));

    public static Task SignOutAsync(HttpContext http) =>
        http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    // True only for a staff session. Endpoints open to guests use this, not
    // User.Identity.IsAuthenticated, before honoring staff-only options.
    public static bool IsStaff(ClaimsPrincipal user) =>
        user.Identity is { IsAuthenticated: true, AuthenticationType: CookieAuthenticationDefaults.AuthenticationScheme };

    public static int EmployeeId(ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);

    public static Employee Current(ClaimsPrincipal user) => new(
        EmployeeId(user),
        user.FindFirstValue(ClaimTypes.GivenName) ?? "",
        user.FindFirstValue(ClaimTypes.Surname) ?? "",
        user.FindFirstValue(ClaimTypes.Email) ?? "",
        user.FindFirstValue(ClaimTypes.Role) ?? EmployeeStore.Staff,
        true, null, null);

    // Cookie events: API callers get { "message": "..." } instead of a redirect to a login page.
    public static void Configure(CookieAuthenticationOptions options, bool isDevelopment)
    {
        options.Cookie.Name = CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // Local runs are plain http://localhost; the live site is HTTPS only.
        options.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;

        options.Events.OnRedirectToLogin = context => WriteMessage(context.Response,
            StatusCodes.Status401Unauthorized, EndedMessage);
        options.Events.OnRedirectToAccessDenied = context => WriteMessage(context.Response,
            StatusCodes.Status403Forbidden, "Only managers can do that.");
        options.Events.OnValidatePrincipal = ValidateAsync;
    }

    private static ClaimsPrincipal CreatePrincipal(Employee employee, string stamp) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, employee.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.GivenName, employee.FirstName),
            new Claim(ClaimTypes.Surname, employee.LastName),
            new Claim(ClaimTypes.Email, employee.Email),
            // Re-read on every session check, so a role change takes effect within a minute.
            new Claim(ClaimTypes.Role, employee.Role),
            new Claim(StampClaim, stamp),
            new Claim(CheckedClaim, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    private static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var user = context.Principal!;
        if (DateTime.TryParse(user.FindFirstValue(CheckedClaim), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var checkedAt) && DateTime.UtcNow - checkedAt < RecheckEvery)
            return;

        var store = context.HttpContext.RequestServices.GetRequiredService<EmployeeStore>();
        var stamp = user.FindFirstValue(StampClaim) ?? "";
        Employee? employee;
        try
        {
            employee = await store.GetSessionAsync(EmployeeId(user), stamp);
        }
        catch (Exception e) when (e is DbException or DatabaseUnavailableException)
        {
            // Keep the session; the request itself will report the database as offline.
            return;
        }

        if (employee == null)
        {
            context.RejectPrincipal();
            await SignOutAsync(context.HttpContext);
            return;
        }
        context.ReplacePrincipal(CreatePrincipal(employee, stamp));
        context.ShouldRenew = true;
    }

    private static Task WriteMessage(HttpResponse response, int status, string message)
    {
        response.StatusCode = status;
        return response.WriteAsJsonAsync(new { message });
    }
}
