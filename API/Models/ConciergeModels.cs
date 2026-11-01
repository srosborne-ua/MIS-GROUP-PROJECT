namespace API.Models;

// POST api/concierge body. The client sends only the recent chat and which
// site section the user is on; the system prompt never leaves the server.
public class ConciergeRequest
{
    public List<ConciergeMessage>? Messages { get; set; }
    public string? Page { get; set; }
    // The open booking card's token, sent only with the first message after the card appears.
    public string? OfferToken { get; set; }
}

public class ConciergeMessage
{
    public string? Role { get; set; }      // "user" or "assistant"
    public string? Content { get; set; }
}
