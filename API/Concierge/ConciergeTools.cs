using System.Text.Json;
using Anthropic.Models.Beta.Messages;

namespace API.Concierge;

// A tool the concierge can call. Run() must go through the same services the
// controllers use (e.g. HotelStore), never write data directly from model output,
// and anything that creates, changes, or cancels a booking must require explicit
// guest or staff confirmation before it runs.
public interface IConciergeTool
{
    BetaTool Definition { get; }   // name, description, input schema (set Strict = true)
    Task<object> Run(IReadOnlyDictionary<string, JsonElement> input, string page);
}

// Tool registry. Empty for now: the concierge only answers questions and
// redirects booking/availability requests to the booking page.
//
// To add a tool later (e.g. check_availability, create_booking), implement
// IConciergeTool and register it in Program.cs:
//     builder.Services.AddSingleton<IConciergeTool, CheckAvailabilityTool>();
// ConciergeService already sends the definitions to the API and runs the tool loop.
public class ConciergeTools(IEnumerable<IConciergeTool> tools)
{
    private readonly List<IConciergeTool> _tools = tools.ToList();

    public List<BetaToolUnion> Definitions() => _tools.Select(t => (BetaToolUnion)t.Definition).ToList();

    public async Task<(string Content, bool IsError)> Run(string name, IReadOnlyDictionary<string, JsonElement> input, string page)
    {
        var tool = _tools.FirstOrDefault(t => t.Definition.Name == name);
        if (tool is null) return ("Unknown tool: " + name, true);
        try
        {
            return (JsonSerializer.Serialize(await tool.Run(input, page)), false);
        }
        catch (Exception ex)
        {
            return ("Tool failed: " + ex.Message, true);
        }
    }
}
