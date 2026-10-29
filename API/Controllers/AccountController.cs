using API.Models;
using API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

// Guest accounts: create one, sign in and out, and see your own stays. The session is the
// druid_guest HttpOnly cookie (see GuestSession), separate from the staff session.
[ApiController]
[Route("api/account")]
public class AccountController(GuestAccountStore accounts, HotelStore store) : ControllerBase
{
    // POST api/account: create an account and sign in.
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("guest-sign-in")]
    public async Task<GuestAccount> Register(GuestRegisterRequest input)
    {
        var (account, stamp) = await accounts.RegisterAsync(input);
        await GuestSession.SignInAsync(HttpContext, account, stamp);
        return account;
    }

    // POST api/account/sign-in
    [HttpPost("sign-in")]
    [AllowAnonymous]
    [EnableRateLimiting("guest-sign-in")]
    public async Task<GuestAccount> SignIn(SignInRequest input)
    {
        var (account, stamp) = await accounts.SignInAsync(input);
        await GuestSession.SignInAsync(HttpContext, account, stamp);
        return account;
    }

    // POST api/account/sign-out
    [HttpPost("sign-out")]
    [AllowAnonymous]
    public async Task<IActionResult> SignOutGuest()
    {
        await GuestSession.SignOutAsync(HttpContext);
        return NoContent();
    }

    // GET api/account/me: the signed-in guest, or 204 when nobody is signed in.
    [HttpGet("me")]
    [AllowAnonymous]
    public Task<GuestAccount?> Me() => GuestSession.CurrentAsync(HttpContext);

    // GET api/account/reservations: the guest's own reservations, latest arrival first.
    [HttpGet("reservations")]
    [Authorize(AuthenticationSchemes = GuestSession.Scheme)]
    public Task<List<Reservation>> GetReservations() =>
        store.ListAccountReservationsAsync(GuestSession.AccountId(User));

    // GET api/account/reservations/DC-1042: one of the guest's own reservations.
    [HttpGet("reservations/{id}")]
    [Authorize(AuthenticationSchemes = GuestSession.Scheme)]
    public Task<Reservation> GetReservation(string id) =>
        store.GetAccountReservationAsync(GuestSession.AccountId(User), id);

    // POST api/account/reservations/DC-1042/pay: pay for an upcoming stay online.
    // The body never contains card details: only the card-on-file opt-in.
    [HttpPost("reservations/{id}/pay")]
    [Authorize(AuthenticationSchemes = GuestSession.Scheme)]
    [EnableRateLimiting("booking")]
    public Task<Reservation> PayReservation(string id, PayReservationRequest input) =>
        store.PayAccountReservationAsync(GuestSession.AccountId(User), id, input.CardOnFile);
}
