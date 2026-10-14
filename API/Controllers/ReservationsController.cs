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
    public List<Reservation> GetReservations() => store.GetReservations();

    // GET api/reservations/DC-1042
    [HttpGet("{id}")]
    public Reservation GetReservation(string id) => store.GetReservation(id);

    // POST api/reservations
    // The body never contains card details: only paid/unpaid and the card-on-file opt-in.
    [HttpPost]
    public Reservation CreateReservation(CreateReservationRequest input) => store.CreateReservation(input);

    // POST api/reservations/DC-1042/cancel
    [HttpPost("{id}/cancel")]
    public Reservation CancelReservation(string id) => store.CancelReservation(id);

    // PUT api/reservations/DC-1042/payment-status
    [HttpPut("{id}/payment-status")]
    public Reservation SetPaymentStatus(string id, PaymentStatusRequest input) => store.SetPaymentStatus(id, input.Status);

    // POST api/reservations/DC-1042/messages
    [HttpPost("{id}/messages")]
    public Reservation SendGuestMessage(string id, GuestMessageRequest input) => store.SendGuestMessage(id, input.Text);
}
