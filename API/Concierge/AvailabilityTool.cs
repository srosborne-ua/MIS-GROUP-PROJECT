using System.Text.Json;
using API.Models;
using API.Services;
using Anthropic.Models.Beta.Messages;

namespace API.Concierge;

// check_availability: open rooms and exact totals for a stay, from the same
// HotelStore search the booking page uses, so prices include weekend nights and
// special rates exactly as a guest would see them. Read-only; it never holds or
// books a room.
public class CheckAvailabilityTool(HotelStore store) : IConciergeTool
{
    public BetaTool Definition { get; } = new()
    {
        Name = "check_availability",
        Description =
            "Finds the rooms open for a stay and the exact total price for each room type, using the hotel's live " +
            "booking rules (weekend nights, special rates, holds, event blocks, existing reservations). Use it whenever a " +
            "guest asks what is available or what a stay would cost on specific dates. Dates are YYYY-MM-DD; checkout is the " +
            "morning they leave. Returns each room type with its open room numbers, the total for the whole stay, and the price " +
            "of each night. It does not hold or book anything.",
        InputSchema = KnowledgeSchemas.Object(new
        {
            checkin = new { type = "string", description = "Arrival date, YYYY-MM-DD." },
            checkout = new { type = "string", description = "Departure date, YYYY-MM-DD (after checkin)." },
            guests = new { type = "integer", description = "Number of guests; use 1 if not given." },
            bed = new { type = "string", @enum = new[] { "any", "king", "queen", "twin" }, description = "Bed type filter (suites have a king bed)." },
            floor = new { type = "string", @enum = new[] { "any", "1", "2", "3" }, description = "Floor filter." }
        }, required: ["checkin", "checkout", "guests", "bed", "floor"]),
        Strict = true
    };

    public async Task<object> Run(IReadOnlyDictionary<string, JsonElement> input, string page)
    {
        string? Text(string key) => input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var query = new AvailabilityQuery
        {
            Checkin = Text("checkin"),
            Checkout = Text("checkout"),
            Guests = input.TryGetValue("guests", out var g) && g.TryGetInt32(out var n) ? n : 1,
            Bed = Text("bed"),
            Floor = Text("floor")
        };

        List<AvailabilityGroup> groups;
        Dictionary<string, RoomType> types;
        try
        {
            groups = await store.SearchAvailabilityAsync(query);
            types = await store.GetRoomTypesAsync();
        }
        catch (HotelRuleException ex)
        {
            // Rule messages are written for guests ("Departure must be after arrival.").
            throw new ConciergeToolException(ex.Message);
        }

        return new
        {
            checkin = query.Checkin,
            checkout = query.Checkout,
            guests = query.Guests,
            rooms_available = groups.Sum(x => x.Rooms.Count),
            room_types = groups.Select(x => new
            {
                type = x.Type,
                name = types.TryGetValue(x.Type, out var t) ? t.Name : x.Type,
                rooms_available = x.Rooms.Count,
                room_numbers = x.Rooms.Select(r => r.Number),
                total = x.Quote.Total,
                nights = x.Quote.Nights.Select(night => new { date = night.Date, amount = night.Amount, kind = night.Kind, label = night.Label })
            }),
            note = "Prices are before taxes. Rooms are not held until the guest books on the booking page."
        };
    }
}
