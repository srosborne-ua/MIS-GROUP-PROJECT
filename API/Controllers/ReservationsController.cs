using API.Models;
using API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Staff only, except creating a reservation, which signed-in guests do from the booking page.
// A guest's own reservations are in AccountController.
[ApiController]
[Route("api/reservations")]
public class ReservationsController(HotelStore store) : ControllerBase
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
    // Staff (source "staff": past arrivals allowed, no payment method) need a staff session;
    // guests need a guest account session and book under their account's email.
    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<Reservation>> CreateReservation(CreateReservationRequest input)
    {
        if (input.Source == "staff")
        {
            if (!StaffSession.IsStaff(User)) return Unauthorized(new { message = StaffSession.EndedMessage });
            return await store.CreateReservationAsync(input, Booker.Staff);
        }
        var guest = await GuestSession.CurrentAsync(HttpContext);
        if (guest == null) return GuestSession.SignInRequired();
        return await store.CreateReservationAsync(input, Booker.Guest(guest.Id));
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
