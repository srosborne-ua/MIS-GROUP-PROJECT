using System.Text.Json;
using API.Models;
using Anthropic.Models.Beta.Messages;

namespace API.Concierge;

// A tool the concierge can call. Run() must go through the same services the
// controllers use (e.g. HotelStore), never write data directly from model output,
// and anything that creates, changes, or cancels a booking must require explicit
// guest or staff confirmation before it runs (see BookingTool.cs: the model proposes, and
// only the guest's button press or their own "yes" to the card books).
public interface IConciergeTool
{
    BetaTool Definition { get; }   // name, description, input schema (set Strict = true)
    Task<object> Run(IReadOnlyDictionary<string, JsonElement> input, ConciergeContext context);
}

// What a tool knows about the request:
//   Page  - the site section ("main", "booking", or "staff")
//   Guest - the signed-in guest account from the druid_guest cookie, or null
//   Emit  - sends an event to the browser outside the reply text, such as the booking
//           card with its Confirm button. The model never sees what is emitted.
//   Offer - the open offer the guest's latest message accepts, or null. confirm_booking is
//           only offered to the model when this is set.
//   BookingActed - this reply already showed a new card or booked, so a card the guest
//           hasn't seen can't be confirmed, and a booked reply can't open another offer.
public record ConciergeContext(string Page, GuestAccount? Guest, Func<object, Task> Emit, ConfirmableOffer? Offer = null)
{
    public bool BookingActed { get; set; }
}

// Thrown by a tool for a failure the model should see. The message is sent to the
// model as-is, so it must never include SQL, stack traces, or connection details.
public class ConciergeToolException(string message) : Exception(message);

// Tool registry. check_availability (AvailabilityTool.cs), propose_booking and
// confirm_booking (BookingTool.cs) are always registered, though confirm_booking is only
// offered when the guest's latest message accepts an open offer. The database query tools
// (KnowledgeTools.cs) are registered when DB_LLM_RO_CONNECTION is set; otherwise the
// concierge answers hotel facts from hotel-info.json in its prompt.
//
// To add a tool, implement IConciergeTool and register it in Program.cs:
//     builder.Services.AddSingleton<IConciergeTool, CheckAvailabilityTool>();
// ConciergeService already sends the definitions to the API and runs the tool loop.
public class ConciergeTools(IEnumerable<IConciergeTool> tools, ILogger<ConciergeTools> logger)
{
    private const string ToolFailed = "That lookup isn't available right now.";

    private readonly List<IConciergeTool> _tools = tools.ToList();

    public List<BetaToolUnion> Definitions(ConciergeContext context) => _tools
        .Where(t => context.Offer != null || t.Definition.Name != ConfirmBookingTool.ToolName)
        .Select(t => (BetaToolUnion)t.Definition)
        .ToList();

    public async Task<(string Content, bool IsError)> Run(string name, IReadOnlyDictionary<string, JsonElement> input, ConciergeContext context)
    {
        var tool = _tools.FirstOrDefault(t => t.Definition.Name == name);
        if (tool is null) return ("Unknown tool: " + name, true);
        try
        {
            return (JsonSerializer.Serialize(await tool.Run(input, context)), false);
        }
        catch (ConciergeToolException ex)
        {
            return (ex.Message, true);
        }
        catch (Exception ex)
        {
            // Database and other failures: log the type only (exception messages can carry
            // SQL or server details) and give the model a generic message.
            logger.LogError("Concierge tool {Tool} failed: {Type}", name, ex.GetType().Name);
            return (ToolFailed, true);
        }
    }
}
