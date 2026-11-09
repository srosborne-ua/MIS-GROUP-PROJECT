using API.Models;
using API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Stripe;
using Stripe.Checkout;

namespace API.Controllers;

public class ConfirmPaymentRequest
{
    public string? SessionId { get; set; }
}

// Online payment through Stripe Checkout (see StripePayments). Card details never touch this server.
[ApiController]
public class PaymentsController(HotelStore store, StripePayments stripe, ILogger<PaymentsController> log) : ControllerBase
{
    // GET api/payments/config: whether real online payment is on. No secrets.
    [HttpGet("api/payments/config")]
    [AllowAnonymous]
    public object Config() => new { online = StripePayments.IsConfigured };

    // POST api/account/reservations/DC-1042/checkout: where to send the guest to pay.
    [HttpPost("api/account/reservations/{id}/checkout")]
    [Authorize(AuthenticationSchemes = GuestSession.Scheme)]
    [EnableRateLimiting("booking")]
    public async Task<object> StartCheckout(string id)
    {
        if (!StripePayments.IsConfigured) throw new HotelRuleException("Online payment isn’t set up yet. You can pay at check-in.");
        var reservation = await store.GetAccountReservationAsync(GuestSession.AccountId(User), id);
        if (reservation.Status == "cancelled") throw new HotelRuleException("That reservation was cancelled, so there’s nothing to pay.");
        if (reservation.PaymentStatus == "paid") throw new HotelRuleException("That stay is already paid in full.");
        if (reservation.Checkout <= HotelStore.Today()) throw new HotelRuleException("That stay has ended. Please settle it with the front desk.");
        var guest = await GuestSession.CurrentAsync(HttpContext) ?? throw new HotelRuleException(GuestSession.SignInMessage);
        var types = await store.GetRoomTypesAsync();
        var roomName = types.TryGetValue(reservation.Type, out var type) ? type.Name : "Room";
        var (url, sessionId) = await stripe.CreateCheckoutAsync(reservation, guest.Email, roomName);
        try
        {
            await store.RememberCheckoutSessionAsync(reservation.Id, sessionId);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Only the "Check payment" shortcut needs this; never block a guest from paying over it.
            log.LogWarning("Couldn’t record the Stripe session for {Reservation}: {Error}", reservation.Id, e.Message);
        }
        return new { url };
    }

    // POST api/account/reservations/DC-1042/checkout/check: "Check payment" for a guest's own stay.
    [HttpPost("api/account/reservations/{id}/checkout/check")]
    [Authorize(AuthenticationSchemes = GuestSession.Scheme)]
    [EnableRateLimiting("booking")]
    public async Task<Reservation> CheckGuestPayment(string id)
    {
        var reservation = await store.GetAccountReservationAsync(GuestSession.AccountId(User), id);
        return await CheckPaymentAsync(reservation);
    }

    // POST api/reservations/DC-1042/check-payment: "Check payment" for staff (signed-in employees only).
    [HttpPost("api/reservations/{id}/check-payment")]
    public async Task<Reservation> CheckStaffPayment(string id) => await CheckPaymentAsync(await store.GetReservationAsync(id));

    // Asks Stripe about the stay's latest Checkout session. Safe to repeat: a paid stay stays paid once.
    private async Task<Reservation> CheckPaymentAsync(Reservation reservation)
    {
        if (reservation.PaymentStatus == "paid") return reservation;
        if (reservation.Status == "cancelled") throw new HotelRuleException("That reservation was cancelled, so there’s nothing to pay.");
        if (!StripePayments.IsConfigured) throw new HotelRuleException("Online payment isn’t set up, so there’s nothing to check.");
        var sessionId = await store.GetCheckoutSessionAsync(reservation.Id);
        if (string.IsNullOrEmpty(sessionId))
            throw new HotelRuleException("No online payment has been started for this stay yet.");
        var paid = await stripe.GetPaidAsync(sessionId);
        if (paid is not { } p || p.ReservationId != reservation.Id)
            throw new HotelRuleException("Stripe hasn’t received that payment yet. If the guest was charged, try again in a minute.");
        return await RecordPaidAsync(reservation.Id, p.AmountCents);
    }

    // MarkPaidOnlineAsync, but a refused payment (for example, the amount doesn’t match) is logged, not silent.
    private async Task<Reservation> RecordPaidAsync(string reservationId, long amountCents)
    {
        try
        {
            return await store.MarkPaidOnlineAsync(reservationId, amountCents);
        }
        catch (HotelRuleException e)
        {
            log.LogWarning("Stripe payment for {Reservation} not recorded: {Reason}", reservationId, e.Message);
            throw;
        }
    }

    // POST api/account/reservations/DC-1042/checkout/confirm: the guest is back from Stripe.
    // Stripe is asked directly whether the session was paid.
    [HttpPost("api/account/reservations/{id}/checkout/confirm")]
    [Authorize(AuthenticationSchemes = GuestSession.Scheme)]
    [EnableRateLimiting("booking")]
    public async Task<Reservation> ConfirmCheckout(string id, ConfirmPaymentRequest input)
    {
        var reservation = await store.GetAccountReservationAsync(GuestSession.AccountId(User), id);
        if (reservation.PaymentStatus == "paid") return reservation;
        var paid = string.IsNullOrWhiteSpace(input.SessionId) ? null : await stripe.GetPaidAsync(input.SessionId.Trim());
        if (paid is not { } p || p.ReservationId != reservation.Id)
            throw new HotelRuleException("We haven’t received that payment yet. If you were charged, it will show up here within a minute or two.");
        return await RecordPaidAsync(reservation.Id, p.AmountCents);
    }

    // POST api/payments/webhook: Stripe tells us a payment finished, even if the guest closed the tab.
    // Anonymous, but every call is checked against the webhook signing secret.
    [HttpPost("api/payments/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        if (!StripePayments.IsConfigured || StripePayments.WebhookSecret == null) return NotFound();
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Event stripeEvent;
        try
        {
            // The signature is still checked. Only the API-version match is relaxed: the account's default
            // version often differs from the SDK's, and the fields read below are stable across versions.
            stripeEvent = EventUtility.ConstructEvent(body, Request.Headers["Stripe-Signature"], StripePayments.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException)
        {
            return BadRequest();
        }
        if (stripeEvent.Data.Object is Session session &&
            (stripeEvent.Type == "checkout.session.completed" || stripeEvent.Type == "checkout.session.async_payment_succeeded") &&
            session.PaymentStatus == "paid" &&
            session.Metadata != null && session.Metadata.TryGetValue(StripePayments.ReservationKey, out var reservationId))
        {
            try
            {
                await store.MarkPaidOnlineAsync(reservationId, session.AmountSubtotal ?? 0);
            }
            catch (HotelRuleException e)
            {
                // Answer 200 so Stripe doesn't retry forever; staff can see the mismatch in the log.
                log.LogWarning("Stripe payment for {Reservation} not recorded: {Reason}", reservationId, e.Message);
            }
        }
        return Ok();
    }
}
