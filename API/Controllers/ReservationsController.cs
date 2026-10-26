using API.Models;
using API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

// Staff only, except creating a reservation, which guests do from the booking page.
[ApiController]
[Route("api/reservations")]
public class ReservationsController(HotelStore store, ConfirmationEmail confirmationEmail) : ControllerBase
{
    // GET api/reservations?status=upcoming&payment=any&q=smith&page=1
    [HttpGet]
    public Task<ReservationPage> GetReservations([FromQuery] ReservationListQuery query) => store.ListReservationsAsync(query);

    // GET api/reservations/stats
    [HttpGet("stats")]
    public Task<ReservationStats> GetStats() => store.GetReservationStatsAsync();

    // GET api/reservations/DC-1042
    [HttpGet("{id}")]
    public Task<Reservation> GetReservation(string id) => store.GetReservationAsync(id);

    // POST api/reservations
    // The body never contains card details: only paid/unpaid and the card-on-file opt-in.
    // source "staff" (past arrivals allowed, no payment method) only counts for signed-in staff.
    // The guest is emailed a receipt either way; a failed email doesn't undo the booking.
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("booking")]
    public async Task<Reservation> CreateReservation(CreateReservationRequest input)
    {
        var reservation = await store.CreateReservationAsync(input, isStaff: input.Source == "staff" && User.Identity?.IsAuthenticated == true);
        await confirmationEmail.SendAsync(reservation);
        return reservation;
    }

    // POST api/reservations/DC-1042/cancel
    [HttpPost("{id}/cancel")]
    public Task<Reservation> CancelReservation(string id) => store.CancelReservationAsync(id);

    // PUT api/reservations/DC-1042/guest
    [HttpPut("{id}/guest")]
    public Task<Reservation> UpdateGuestDetails(string id, UpdateGuestDetailsRequest input) => store.UpdateGuestDetailsAsync(id, input);

    // PUT api/reservations/DC-1042/payment-status
    [HttpPut("{id}/payment-status")]
    public Task<Reservation> SetPaymentStatus(string id, PaymentStatusRequest input) => store.SetPaymentStatusAsync(id, input.Status);

    // POST api/reservations/DC-1042/messages
    [HttpPost("{id}/messages")]
    public Task<Reservation> SendGuestMessage(string id, GuestMessageRequest input) => store.SendGuestMessageAsync(id, input.Text);
}
