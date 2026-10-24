using API.Models;
using Anthropic;
using Anthropic.Helpers;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace API.Concierge;

// Thrown for a malformed chat request; the controller returns it as a 400.
public class ConciergeInputException(string message) : Exception(message);

public enum ConciergeStatus { Ok, Refusal, MaxTokens }

// Druid AI Assistant: validates the chat history from the browser, calls Claude
// through the Anthropic SDK, and streams the reply back through callbacks.
// The request loop already handles tool_use turns, so tools registered with
// ConciergeTools work without restructuring this class.
public class ConciergeService(ConciergePrompt prompt, ConciergeTools tools)
{
    public const int MaxHistory = 10;          // rolling window of messages sent to the model
    private const int MaxMessageChars = 2000;  // per message
    private const int MaxTokens = 2048;        // short chat replies; includes thinking
    private const int MaxToolTurns = 8;        // schema, a query or two, availability, then the answer
    private const int HoldBackChars = 200;     // see Run(): longer than a pre-tool note, short enough to feel instant

    public static readonly string Model = Environment.GetEnvironmentVariable("CONCIERGE_MODEL") is { Length: > 0 } m
        ? m
        : "claude-opus-5-5";

    // Server-side refusal fallback: if the model's safety classifiers decline, the API
    // re-runs the request on a recommended fallback model in the same call. Only sent
    // for models that support the "default" fallback mode.
    private static readonly bool UseFallbacks =
        Model.StartsWith("claude-opus-5") || Model.StartsWith("claude-fable-5") || Model.StartsWith("claude-sonnet-5-5");

    // Reads ANTHROPIC_API_KEY from the environment.
    private readonly AnthropicClient _client = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

    // Keeps only the last MaxHistory messages, merges consecutive same-role turns,
    // and guarantees the conversation starts and ends with a user message.
    public static List<BetaMessageParam> CleanHistory(List<ConciergeMessage>? raw)
    {
        if (raw is null || raw.Count == 0) throw new ConciergeInputException("messages must be a non-empty array");

        var turns = new List<(string Role, string Content)>();
        foreach (var m in raw.TakeLast(MaxHistory))
        {
            if (m is null || (m.Role != "user" && m.Role != "assistant") || m.Content is null)
                throw new ConciergeInputException("each message needs a role of user or assistant and string content");
            var content = m.Content.Trim();
            if (content.Length > MaxMessageChars) content = content[..MaxMessageChars];
            if (content.Length == 0) continue;
            if (turns.Count > 0 && turns[^1].Role == m.Role) turns[^1] = (m.Role, turns[^1].Content + "\n\n" + content);
            else turns.Add((m.Role, content));
        }

        while (turns.Count > 0 && turns[0].Role != "user") turns.RemoveAt(0);
        if (turns.Count == 0 || turns[^1].Role != "user") throw new ConciergeInputException("the last message must be from the user");

        return turns.Select(t => new BetaMessageParam
        {
            Role = t.Role == "user" ? Role.User : Role.Assistant,
            Content = t.Content
        }).ToList();
    }

    // Runs one concierge reply.
    //   onText(delta) - a chunk of reply text to show
    //   onReset()     - discard text shown so far (the model declined mid-reply and a
    //                   fallback model is starting over)
    public async Task<ConciergeStatus> Run(List<BetaMessageParam> messages, string page,
        Func<string, Task> onText, Func<Task> onReset, CancellationToken cancellationToken)
    {
        var system = prompt.Build(page).Select(text => new BetaTextBlockParam { Text = text }).ToList();
        var toolDefinitions = tools.Definitions();
        var conversation = new List<BetaMessageParam>(messages);
        var shownText = false;  // text already sent in an earlier turn of this reply

        // A turn's opening text is held back until it is clearly a reply: the model sometimes
        // writes a short note ("Next Friday is Oct 30") before calling a tool, and that note
        // is dropped when the tool call starts. Longer text, or a turn that ends without a
        // tool call, is shown.
        var pending = new System.Text.StringBuilder();
        async Task Flush()
        {
            if (pending.Length == 0) return;
            if (shownText) await onText("\n\n");  // separate text from an earlier turn
            shownText = true;
            await onText(pending.ToString());
            pending.Clear();
        }

        for (var turn = 0; turn < MaxToolTurns; turn++)
        {
            var turnText = false;  // this turn's text is already streaming
            pending.Clear();
            var parameters = new MessageCreateParams
            {
                Model = Model,
                MaxTokens = MaxTokens,
                System = system,
                Messages = conversation,
                OutputConfig = new BetaOutputConfig { Effort = Effort.Low },  // chat route: fast, concise answers
                Tools = toolDefinitions.Count > 0 ? toolDefinitions : null,
                Fallbacks = UseFallbacks ? new BetaFallbacksParam(new Default()) : null,
                Betas = UseFallbacks ? [AnthropicBeta.ServerSideFallback2026_07_01] : null
            };

            var aggregator = new BetaMessageContentAggregator();
            var stream = _client.Beta.Messages.CreateStreaming(parameters, cancellationToken);
            await foreach (var rawEvent in aggregator.CollectAsync(stream).WithCancellation(cancellationToken))
            {
                if (rawEvent.TryPickContentBlockStart(out var start) && start.ContentBlock.TryPickBetaFallback(out _))
                {
                    shownText = turnText = false;
                    pending.Clear();
                    await onReset();
                }
                else if (rawEvent.TryPickContentBlockStart(out var toolStart) && toolStart.ContentBlock.TryPickBetaToolUse(out _))
                {
                    pending.Clear();
                }
                else if (rawEvent.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
                {
                    if (turnText)
                    {
                        await onText(text.Text);
                        continue;
                    }
                    pending.Append(text.Text);
                    if (pending.Length >= HoldBackChars)
                    {
                        await Flush();
                        turnText = true;
                    }
                }
            }
            var message = aggregator.Message();

            var stopReason = message.StopReason?.Raw();
            if (stopReason != "tool_use") await Flush();
            if (stopReason == "refusal") return ConciergeStatus.Refusal;
            if (stopReason != "tool_use") return stopReason == "max_tokens" ? ConciergeStatus.MaxTokens : ConciergeStatus.Ok;

            // Tool turn: run every requested tool and return all results in one user message.
            var toolUses = message.Content.Select(b => b.Value).OfType<BetaToolUseBlock>().ToList();
            var results = new List<BetaContentBlockParam>();
            foreach (var use in toolUses)
            {
                var (content, isError) = await tools.Run(use.Name, use.Input, page);
                results.Add(new BetaToolResultBlockParam { ToolUseID = use.ID, Content = content, IsError = isError });
            }
            conversation.Add(new BetaMessageParam { Role = Role.Assistant, Content = ToParams(message.Content) });
            conversation.Add(new BetaMessageParam { Role = Role.User, Content = results });
        }
        return ConciergeStatus.Ok;
    }

    // Response blocks are not request blocks in the C# SDK, so convert the ones a
    // tool turn can contain before sending the assistant turn back unchanged.
    private static List<BetaContentBlockParam> ToParams(IReadOnlyList<BetaContentBlock> blocks)
    {
        var result = new List<BetaContentBlockParam>();
        foreach (var block in blocks)
        {
            if (block.TryPickText(out var text)) result.Add(new BetaTextBlockParam { Text = text.Text });
            else if (block.TryPickThinking(out var thinking)) result.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
            else if (block.TryPickRedactedThinking(out var redacted)) result.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
            else if (block.TryPickToolUse(out var use)) result.Add(new BetaToolUseBlockParam { ID = use.ID, Name = use.Name, Input = use.Input });
        }
        return result;
    }
}
