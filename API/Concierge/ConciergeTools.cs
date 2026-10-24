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

// Thrown by a tool for a failure the model should see. The message is sent to the
// model as-is, so it must never include SQL, stack traces, or connection details.
public class ConciergeToolException(string message) : Exception(message);

// Tool registry. check_availability (AvailabilityTool.cs) is always registered; the
// database query tools (KnowledgeTools.cs) are registered when DB_LLM_RO_CONNECTION is
// set; otherwise the concierge answers hotel facts from hotel-info.json in its prompt.
//
// To add a tool, implement IConciergeTool and register it in Program.cs:
//     builder.Services.AddSingleton<IConciergeTool, CheckAvailabilityTool>();
// ConciergeService already sends the definitions to the API and runs the tool loop.
public class ConciergeTools(IEnumerable<IConciergeTool> tools, ILogger<ConciergeTools> logger)
{
    private const string ToolFailed = "That lookup isn't available right now.";

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
