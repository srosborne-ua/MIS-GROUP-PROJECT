using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using API.Models;
using API.Services;
using Microsoft.AspNetCore.DataProtection;

namespace API.Concierge;

// The booking card the chat shows. Token goes to the browser only, never to the model.
public record BookingProposal(string Token, int Room, int Floor, string Type, string TypeName, string BedLabel,
    DateOnly Checkin, DateOnly Checkout, int Guests, string Requests, string GuestName,
    List<NightRate> Nights, decimal Total, int ExpiresInSeconds);

// Concierge bookings happen in two steps, so the model can never book on its own:
//   1. propose_booking (called by the model) re-checks the room and prices the stay, and
//      CreateAsync signs that exact offer into a token: account, room, dates, guests,
//      requests, and total, good for 10 minutes. The browser shows it as a card with a
//      Confirm booking button.
//   2. The guest presses the button. The browser posts the token to
//      POST api/concierge/bookings, and ConfirmAsync books it through
//      HotelStore.CreateReservationAsync, which locks the room, re-checks it, and refuses
//      if the total changed since the guest saw it (BookingConflictException).
// Tokens are signed with ASP.NET Core Data Protection (the same keys as the sign-in
// cookies), so they can't be edited or forged, and only work for the account they were made for.
public class BookingProposals(HotelStore store, IDataProtectionProvider protection)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const string Expired = "This offer has expired. Ask the assistant again and it will recheck the room and price.";
    private const int MaxRequestChars = 1000;
    private const int MaxTokenChars = 4000;

    private readonly ITimeLimitedDataProtector _protector =
        protection.CreateProtector("API.Concierge.BookingProposal.v1").ToTimeLimitedDataProtector();

    // Offers already confirmed (or being confirmed), so a second press can't book twice. In
    // memory, like ConciergeLimits; an entry is dropped once its offer has expired anyway.
    private readonly ConcurrentDictionary<Guid, DateTime> _used = new();

    private record Offer(Guid Id, int AccountId, int Room, string Type, DateOnly Checkin, DateOnly Checkout,
        int Guests, string Requests, decimal Total);

    // Throws HotelRuleException (in the guest's voice) when the room can't be offered.
    public async Task<BookingProposal> CreateAsync(GuestAccount guest, int room, string? checkin, string? checkout,
        int guests, string? requests)
    {
        var note = (requests ?? "").Trim();
        if (note.Length > MaxRequestChars) throw new HotelRuleException("Keep requests under 1,000 characters.");
        var check = await store.CheckRoomAsync(room, checkin, checkout, allowPast: false);
        if (!check.Available) throw new HotelRuleException("Room " + room + " isn’t open for those dates.");
        var type = (await store.GetRoomTypesAsync())[check.Room.Type];
        guests = Math.Max(guests, 1);
        if (guests > type.Sleeps) throw new HotelRuleException("Room " + room + " sleeps " + type.Sleeps + ".");

        var nights = check.Quote.Nights;
        var offer = new Offer(Guid.NewGuid(), guest.Id, room, check.Room.Type, nights[0].Date, nights[^1].Date.AddDays(1),
            guests, note, check.Quote.Total);
        var token = _protector.Protect(JsonSerializer.Serialize(offer), Lifetime);
        return new BookingProposal(token, room, check.Room.Floor, check.Room.Type, type.Name, type.BedLabel,
            offer.Checkin, offer.Checkout, guests, note, guest.FirstName + " " + guest.LastName,
            nights, offer.Total, (int)Lifetime.TotalSeconds);
    }

    // Books the offer the guest confirmed.
    public async Task<Reservation> ConfirmAsync(GuestAccount guest, string? token)
    {
        var offer = Read(guest, token) ?? throw new HotelRuleException(Expired);
        Prune();
        if (!_used.TryAdd(offer.Id, DateTime.UtcNow + Lifetime))
            throw new HotelRuleException("That booking is already confirmed. You’ll find it under Your stays.");
        try
        {
            return await store.CreateReservationAsync(new CreateReservationRequest
            {
                Room = offer.Room,
                Checkin = Iso(offer.Checkin),
                Checkout = Iso(offer.Checkout),
                Guests = offer.Guests,
                Guest = new Guest
                {
                    FirstName = guest.FirstName,
                    LastName = guest.LastName,
                    Phone = guest.Phone,
                    Requests = offer.Requests
                },
                PaymentStatus = "unpaid"
            }, Booker.Concierge(guest.Id, offer.Total));
        }
        catch
        {
            _used.TryRemove(offer.Id, out _);
            throw;
        }
    }

    // After a conflict (the room was taken, or the price changed, since the card was shown):
    // what to tell the guest, and a fresh offer for the same stay at today's price. That's
    // the same room if it's still open, otherwise the same kind of room, or null if neither is.
    public async Task<(string Message, BookingProposal? Proposal)> AfterConflictAsync(GuestAccount guest, string? token, string reason)
    {
        if (Read(guest, token) is not { } offer) return (reason + " " + Expired, null);
        string checkin = Iso(offer.Checkin), checkout = Iso(offer.Checkout);
        try
        {
            var same = await CreateAsync(guest, offer.Room, checkin, checkout, offer.Guests, offer.Requests);
            return (reason + " Room " + same.Room + " is still yours to book at the new total of " + HotelStore.Money(same.Total) +
                    ". Confirm it below if you’d still like it.", same);
        }
        catch (HotelRuleException)
        {
            // Taken: look for another room of the same kind.
        }
        var groups = await store.SearchAvailabilityAsync(new AvailabilityQuery { Checkin = checkin, Checkout = checkout, Guests = offer.Guests });
        if (groups.Find(g => g.Type == offer.Type) is not { } group)
            return (reason + " Nothing else like it is open for those dates; ask the assistant for other options.", null);
        var other = await CreateAsync(guest, group.Suggested.Number, checkin, checkout, offer.Guests, offer.Requests);
        return (reason + " Room " + other.Room + " is the same kind of room, for " + HotelStore.Money(other.Total) +
                " in total. Confirm it below if you’d like it.", other);
    }

    // The offer in a token, or null if it's expired, altered, or made for another account.
    private Offer? Read(GuestAccount guest, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenChars) return null;
        try
        {
            var offer = JsonSerializer.Deserialize<Offer>(_protector.Unprotect(token));
            return offer?.AccountId == guest.Id ? offer : null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    private void Prune()
    {
        var now = DateTime.UtcNow;
        foreach (var (id, expires) in _used)
            if (expires < now) _used.TryRemove(id, out _);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
