using API.Models;
using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/reservations")]
public class ReservationsController(HotelStore store) : ControllerBase
{
    // GET api/reservations
    [HttpGet]
    public Task<List<Reservation>> GetReservations() => store.GetReservationsAsync();

    // GET api/reservations/DC-1042
    [HttpGet("{id}")]
    public Task<Reservation> GetReservation(string id) => store.GetReservationAsync(id);

    // POST api/reservations
    // The body never contains card details: only paid/unpaid and the card-on-file opt-in.
    [HttpPost]
    public Task<Reservation> CreateReservation(CreateReservationRequest input) => store.CreateReservationAsync(input);

    // POST api/reservations/DC-1042/cancel
    [HttpPost("{id}/cancel")]
    public Task<Reservation> CancelReservation(string id) => store.CancelReservationAsync(id);

    // PUT api/reservations/DC-1042/payment-status
    [HttpPut("{id}/payment-status")]
    public Task<Reservation> SetPaymentStatus(string id, PaymentStatusRequest input) => store.SetPaymentStatusAsync(id, input.Status);

    // POST api/reservations/DC-1042/messages
    [HttpPost("{id}/messages")]
    public Task<Reservation> SendGuestMessage(string id, GuestMessageRequest input) => store.SendGuestMessageAsync(id, input.Text);
}
