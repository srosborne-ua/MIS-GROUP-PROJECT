using System.Text.Json;
using API.Concierge;
using API.Models;
using API.Services;
using Anthropic.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

// Druid AI Assistant. Streams the reply as newline-delimited JSON events:
// { "type": "text", "text": "..." } | { "type": "reset" } | { "type": "done" } | { "type": "error", "message": "..." }
// | { "type": "proposal", "proposal": { ... } }   a booking card to confirm (BookingProposals.cs)
// | { "type": "sign_in" }                         booking needs the guest to sign in first
[ApiController]
[Route("api/concierge")]
public class ConciergeController(ConciergeService concierge, ConciergeLimits limits, BookingProposals proposals,
    ILogger<ConciergeController> logger) : ControllerBase
{
    private const string FriendlyError = "Sorry, I'm having trouble answering right now. Please try again in a moment, or call the front desk.";
    private const string RefusalMessage = "I'm sorry, I can't help with that. I'm happy to answer questions about your stay or booking.";

    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

    // POST api/concierge
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("concierge")]
    [RequestSizeLimit(32 * 1024)]
    public async Task Ask(ConciergeRequest input)
    {
        if (!ConciergeService.IsConfigured)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await Response.WriteAsJsonAsync(new { message = "The concierge is not configured." });
            return;
        }

        List<Anthropic.Models.Beta.Messages.BetaMessageParam> messages;
        try
        {
            messages = ConciergeService.CleanHistory(input.Messages);
        }
        catch (ConciergeInputException ex)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { message = ex.Message });
            return;
        }

        var visitor = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var blocked = limits.TryEnter(visitor);
        if (blocked != ConciergeBlock.None)
        {
            logger.LogWarning("Concierge: message refused ({Limit})", blocked);
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await Response.WriteAsJsonAsync(new { message = ConciergeLimits.Message(blocked) });
            return;
        }

        try
        {
            // The guest session, if any, so propose_booking knows who would be booking.
            var guest = await GuestSession.CurrentAsync(HttpContext);
            await Stream(ConciergePrompt.NormalizePage(input.Page), guest, messages);
        }
        finally
        {
            limits.Leave(visitor);
        }
    }

    // POST api/concierge/bookings: the guest pressed Confirm booking on a card in the chat.
    // Only this button books; the model can't call it. A room taken (or repriced) since the
    // card was shown returns 409 with a fresh card (same room at the new price, or the same kind of room).
    [HttpPost("bookings")]
    [Authorize(AuthenticationSchemes = GuestSession.Scheme)]
    [EnableRateLimiting("booking")]
    public async Task<ActionResult<Reservation>> ConfirmBooking(ConfirmBookingRequest input)
    {
        var guest = GuestSession.Current(User);
        try
        {
            return await proposals.ConfirmAsync(guest, input.Token);
        }
        catch (BookingConflictException ex)
        {
            var (message, proposal) = await proposals.AfterConflictAsync(guest, input.Token, ex.Message);
            return Conflict(new { message, proposal });
        }
    }

    private async Task Stream(string page, GuestAccount? guest, List<Anthropic.Models.Beta.Messages.BetaMessageParam> messages)
    {
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-store";
        var aborted = HttpContext.RequestAborted;

        async Task Send(object e)
        {
            await Response.WriteAsync(JsonSerializer.Serialize(e, EventJson) + "\n", aborted);
            await Response.Body.FlushAsync(aborted);
        }

        try
        {
            var context = new ConciergeContext(page, guest, Send);
            var status = await concierge.Run(messages, context,
                text => Send(new { type = "text", text }),
                () => Send(new { type = "reset" }),
                aborted);
            if (status == ConciergeStatus.Refusal)
            {
                await Send(new { type = "reset" });
                await Send(new { type = "text", text = RefusalMessage });
            }
            await Send(new { type = "done" });
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            // The visitor closed the chat or the page; nothing to send.
        }
        catch (Exception ex)
        {
            // Log status and type only; never the conversation or credentials.
            switch (ex)
            {
                case AnthropicRateLimitException:
                    logger.LogWarning("Concierge: Anthropic rate limit (429)");
                    break;
                case AnthropicUnauthorizedException:
                    logger.LogError("Concierge: API key rejected (401). Check ANTHROPIC_API_KEY in .env");
                    break;
                case AnthropicApiException:
                    logger.LogError("Concierge: Anthropic API error {Type}", ex.GetType().Name);
                    break;
                default:
                    logger.LogError("Concierge: unexpected error {Type}", ex.GetType().Name);
                    break;
            }
            if (!aborted.IsCancellationRequested) await Send(new { type = "error", message = FriendlyError });
        }
    }
}
