using System.Text.Json;
using API.Models;
using API.Services;
using Anthropic.Models.Beta.Messages;

namespace API.Concierge;

// propose_booking: the concierge's half of a booking. It re-checks the room, prices the
// stay, and shows the guest a card with a Confirm booking button (BookingProposals.cs).
// It never books: the guest does, with the button (POST api/concierge/bookings) or by
// replying yes (ConfirmBookingTool below).
public class ProposeBookingTool(BookingProposals proposals) : IConciergeTool
{
    public BetaTool Definition { get; } = new()
    {
        Name = "propose_booking",
        Description =
            "Prepares a reservation for the guest to confirm. It re-checks that the room is still open for those dates and " +
            "prices the stay with the live booking rules, then shows the guest a card in the chat with the details and a " +
            "Confirm booking button. Nothing is booked until the guest presses that button or replies yes to the card. " +
            "Call it only when the guest has said they want to book, with dates they gave or agreed to. " +
            "Use the suggested_room from check_availability unless the guest asked for a particular room. Payment is due at " +
            "check-in unless the guest pays online after confirming (a Pay now button). Returns the prepared details, or an error saying why it couldn't (for example the room was just " +
            "taken, or the guest needs to sign in first).",
        InputSchema = KnowledgeSchemas.Object(new
        {
            room = new { type = "integer", description = "Room number, e.g. 314." },
            checkin = new { type = "string", description = "Arrival date, YYYY-MM-DD." },
            checkout = new { type = "string", description = "Departure date, YYYY-MM-DD (after checkin)." },
            guests = new { type = "integer", description = "Number of guests." },
            special_requests = new
            {
                type = "string",
                description = "Anything the guest asked you to pass on to the hotel (arrival time, an occasion), in a short sentence; " +
                              "an empty string if none. Never card or payment details."
            }
        }, required: ["room", "checkin", "checkout", "guests", "special_requests"]),
        Strict = true
    };

    public async Task<object> Run(IReadOnlyDictionary<string, JsonElement> input, ConciergeContext context)
    {
        if (context.Page == "staff")
            throw new ConciergeToolException("Booking in the chat is for guests. On the employee portal, staff create reservations on the New reservation tab.");
        if (context.Guest is not { } guest)
        {
            await context.Emit(new { type = "sign_in" });
            throw new ConciergeToolException(
                "Nothing was booked or held: the guest isn't signed in, and booking needs a hotel account. The chat now shows " +
                "them a button to sign in or create one (it takes a minute). Ask them to do that and then come back to this " +
                "chat, which will still be here, and you'll set the booking up.");
        }

        if (context.BookingActed)
            throw new ConciergeToolException("A booking was already handled in this reply, so no new card was shown. Tell the guest where things stand.");

        string? Text(string key) => input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int Number(string key, int fallback) =>
            input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;

        BookingProposal proposal;
        try
        {
            proposal = await proposals.CreateAsync(guest, Number("room", 0), Text("checkin"), Text("checkout"),
                Number("guests", 1), Text("special_requests"));
        }
        catch (HotelRuleException ex)
        {
            // Rule messages are written for guests ("Room 214 isn't open for those dates.").
            throw new ConciergeToolException(ex.Message + " Nothing was booked. Use check_availability to find another room if needed.");
        }

        context.BookingActed = true;
        await context.Emit(new { type = "proposal", proposal });
        return new
        {
            status = "waiting_for_guest_confirmation",
            room = proposal.Room,
            floor = proposal.Floor,
            room_type = proposal.TypeName,
            checkin = proposal.Checkin,
            checkout = proposal.Checkout,
            nights = proposal.Nights.Select(n => new { date = n.Date, amount = n.Amount, kind = n.Kind, label = n.Label }),
            guests = proposal.Guests,
            special_requests = proposal.Requests,
            total = proposal.Total,
            payment = "Due at check-in. Once they confirm, the card also offers Pay now, to pay by card online instead.",
            note = "NOT booked yet. The guest now sees these details on a card with a Confirm booking button, good for 10 minutes. " +
                   "In one or two short sentences, say what you've set up (room, dates, total before taxes), and end with: " +
                   "Reply yes or press Confirm booking. Ask nothing else, so their yes can only mean this booking. " +
                   "Don't say it is booked, and don't list every detail; the card shows them."
        };
    }
}

// confirm_booking: books the open offer after the guest replies yes to its card. The model
// only gets this tool when BookingProposals.Confirmable has agreed the guest's own latest
// message accepts a live offer, and it takes no arguments, so the model can't change the room,
// dates, or price. The token stays in ConciergeContext and never reaches the model.
public class ConfirmBookingTool(BookingProposals proposals) : IConciergeTool
{
    public const string ToolName = "confirm_booking";

    public BetaTool Definition { get; } = new()
    {
        Name = ToolName,
        Description =
            "Books the open offer described in your instructions, exactly as the guest's card shows it. Call it only when the " +
            "guest's latest message plainly accepts that offer with no change, condition, or new request. Returns the " +
            "confirmation number, or an error saying why nothing was booked.",
        InputSchema = KnowledgeSchemas.Object(new { }, required: []),
        Strict = true
    };

    public async Task<object> Run(IReadOnlyDictionary<string, JsonElement> input, ConciergeContext context)
    {
        if (context.Page == "staff" || context.Guest is not { } guest || context.Offer is not { } offer || context.BookingActed)
            throw new ConciergeToolException("Nothing was booked: there's no open offer for the guest to accept. Use propose_booking to show them a card.");

        Reservation reservation;
        try
        {
            reservation = await proposals.ConfirmAsync(guest, offer.Token);
        }
        catch (BookingConflictException ex)
        {
            // Taken or repriced since the card was shown: the same fresh card the button gets.
            var (message, fresh) = await proposals.AfterConflictAsync(guest, offer.Token, ex.Message);
            if (fresh != null)
            {
                context.BookingActed = true;
                await context.Emit(new { type = "proposal", proposal = fresh });
            }
            throw new ConciergeToolException("Nothing was booked. " + message);
        }
        catch (HotelRuleException ex)
        {
            throw new ConciergeToolException(ex.Message + " Nothing new was booked.");
        }

        context.BookingActed = true;
        // Sent before the model's reply, so the card shows Booked even if that reply fails.
        await context.Emit(new { type = "booked", reservation });
        return new
        {
            status = "booked",
            confirmation = reservation.Id,
            room = reservation.Room,
            checkin = reservation.Checkin,
            checkout = reservation.Checkout,
            total = reservation.Total,
            payment = "Due at check-in. The card now shows a Pay now button, to pay by card online instead.",
            note = "Booked. In one or two short sentences, start with \"You're booked\" and give the confirmation number. " +
                   "Don't list every detail; the card shows them."
        };
    }
}
