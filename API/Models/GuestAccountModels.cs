namespace API.Models;

// Guest sign-in. Guests need an account to book on the site or through the concierge.
// The password hash never leaves the server.
public record GuestAccount(int Id, string FirstName, string LastName, string Email, string Phone);

public class GuestRegisterRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Password { get; set; }
}

// The Confirm booking button in the concierge chat sends back the token from the
// proposal it showed (see Concierge/BookingProposals.cs).
public class ConfirmBookingRequest
{
    public string? Token { get; set; }
}

// Paying for a stay online. Card details are checked in the browser and discarded;
// only the card-on-file opt-in reaches the server.
public class PayReservationRequest
{
    public bool CardOnFile { get; set; }
}
