namespace API.Models;

// JSON shapes match what the client in Client/Resources/scripts/index.js expects.
// Dates are DateOnly ("yyyy-MM-dd"); timestamps are UTC DateTime.

public record RoomType(string Name, string Bed, string BedLabel, int Sleeps, string Size, string Blurb);

public record Room(int Number, int Floor, string Type);

public record RoomSummary(int Number, int Floor);

// Default nightly rates for one room type. Weekend = Friday and Saturday nights.
public record TypeRate(decimal Weekday, decimal Weekend);

public record RateOverride(DateOnly Date, decimal Amount, string Label);

// Rates: keyed by room type code (king, queen, twin, suite).
public record RatesResponse(Dictionary<string, TypeRate> Rates, List<RateOverride> Overrides);

// kind: "weekday" | "weekend" | "special". Label is only set for special rates.
public record NightRate(DateOnly Date, decimal Amount, string Kind, string? Label = null);

public record Quote(List<NightRate> Nights, decimal Total);

// Suggested: the room offered first (see HotelStore.SuggestRoom); always one of Rooms.
public record AvailabilityGroup(string Type, List<RoomSummary> Rooms, Quote Quote, RoomSummary Suggested);

public record RoomCheck(Room Room, bool Available, Quote Quote);

// Types: the night's rate for each room type, keyed by type code.
public record RatePreviewDay(DateOnly Date, Dictionary<string, NightRate> Types);

public class Guest
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Requests { get; set; } = "";
}

public record GuestMessage(DateTime At, string From, string Text);

// Card details never reach the server: only paid/unpaid and the card-on-file opt-in.
public class Reservation
{
    public string Id { get; set; } = "";
    public int Room { get; set; }
    public string Type { get; set; } = "";
    public DateOnly Checkin { get; set; }
    public DateOnly Checkout { get; set; }
    public int Guests { get; set; }
    public Guest Guest { get; set; } = new();
    public List<NightRate> Nights { get; set; } = [];
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "unpaid";
    public bool CardOnFile { get; set; }
    public string Source { get; set; } = "web";
    public string Status { get; set; } = "confirmed";
    public DateTime CreatedAt { get; set; }
    public List<GuestMessage> Messages { get; set; } = [];
}

// One page of the staff reservation list. Items leave Nights and Messages empty;
// GET api/reservations/{id} has the full reservation.
public record ReservationPage(List<Reservation> Items, int Total, int Page, int PageSize);

public record ReservationStats(int Arrivals, int InHouse, decimal Outstanding);

// Holds and blocks store an inclusive last night in End.
public record Hold(string Id, int Room, DateOnly Start, DateOnly End, string Reason, DateTime CreatedAt);

public record Block(string Id, string Name, DateOnly Start, DateOnly End, List<int> Rooms, DateTime CreatedAt);

public record RoomBoardEntry(int Number, int Floor, string Type, string Status, string Detail);

public record ScheduleItem(string Kind, string Id, DateOnly Start, DateOnly End, string Label);

/* ---------- Request bodies ----------
   Dates arrive as strings so the store can return the same friendly
   validation messages the client used to show. */

public class AvailabilityQuery
{
    public string? Checkin { get; set; }
    public string? Checkout { get; set; }
    public int? Guests { get; set; }
    public string? Bed { get; set; }
    public string? Floor { get; set; }
    public bool AllowPast { get; set; }
}

// Rates: every room type's default rates, keyed by type code.
public class SaveRatesRequest
{
    public Dictionary<string, RateInput>? Rates { get; set; }
}

public class RateInput
{
    public decimal? Weekday { get; set; }
    public decimal? Weekend { get; set; }
}

public class RateOverrideRequest
{
    public string? Date { get; set; }
    public decimal? Amount { get; set; }
    public string? Label { get; set; }
}

public class CreateReservationRequest
{
    public int? Room { get; set; }
    public string? Checkin { get; set; }
    public string? Checkout { get; set; }
    public int? Guests { get; set; }
    public Guest? Guest { get; set; }
    public string? PaymentStatus { get; set; }
    public bool CardOnFile { get; set; }
    public string? Source { get; set; }
}

// Staff reservation list filters. Status: upcoming, past, cancelled, or all.
// Payment: paid, unpaid, or any.
public class ReservationListQuery
{
    public string? Status { get; set; }
    public string? Payment { get; set; }
    public string? Q { get; set; }
    public int? Page { get; set; }
}

// The email stays as booked, since it links the booking to its guest record.
public class UpdateGuestDetailsRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Requests { get; set; }
    public int? Guests { get; set; }
}

public class PaymentStatusRequest
{
    public string? Status { get; set; }
}

public class GuestMessageRequest
{
    public string? Text { get; set; }
}

public class CreateHoldRequest
{
    public int? Room { get; set; }
    public string? Start { get; set; }
    public string? End { get; set; }
    public string? Reason { get; set; }
}

public class CreateBlockRequest
{
    public string? Name { get; set; }
    public string? Start { get; set; }
    public string? End { get; set; }
    public List<int>? Rooms { get; set; }
}
