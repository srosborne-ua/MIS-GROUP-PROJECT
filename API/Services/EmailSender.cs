using Azure;
using Azure.Communication.Email;

namespace API.Services;

// Sends plain-text-plus-HTML email through Azure Communication Services Email. Everything
// comes from environment variables; with either of the two ACS ones unset, IsConfigured is
// false and Send does nothing, so the site keeps working without email. The connection string
// holds an access key: it is never logged.
//   ACS_CONNECTION_STRING  (secret) druid-city-comms > Settings > Keys > Primary connection string
//   ACS_SENDER_ADDRESS     DoNotReply@<domain>.azurecomm.net (a plain address, no "Name <...>")
//   EMAIL_REPLY_TO         optional: where a guest's reply goes; unset keeps replies unmonitored
//   SITE_URL               the site's public address, for links in emails
// Every message is a do-not-reply notice: it is marked automated so mail servers don't send
// vacation replies back. The sender's display name is set on the domain in the Azure portal.
public class EmailSender(ILogger<EmailSender> log)
{
    private static string? Env(string name) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ? null : Environment.GetEnvironmentVariable(name)!.Trim();

    public static bool IsConfigured => Env("ACS_CONNECTION_STRING") != null && Env("ACS_SENDER_ADDRESS") != null;

    // The public address of the site, for links in emails. Never taken from the request.
    public static string SiteUrl => (Env("SITE_URL") ?? "http://localhost:5269").TrimEnd('/');

    // One client for the app's lifetime, built on first use (EmailSender is a singleton).
    private EmailClient? client;
    private EmailClient Client => client ??= new EmailClient(Env("ACS_CONNECTION_STRING")!);

    // True only when Azure accepted the message for delivery.
    public async Task<bool> SendAsync(string to, string subject, string text, string html)
    {
        if (!IsConfigured) return false;
        try
        {
            var message = new EmailMessage(Env("ACS_SENDER_ADDRESS")!, to,
                new EmailContent(subject) { PlainText = text, Html = html });
            if (Env("EMAIL_REPLY_TO") is { } replyTo)
                message.ReplyTo.Add(new EmailAddress(replyTo));
            // Automated mail: tells servers not to auto-reply (RFC 3834).
            message.Headers.Add("Auto-Submitted", "auto-generated");
            message.Headers.Add("X-Auto-Response-Suppress", "All");

            // Started: returns once Azure has accepted the message, without waiting on delivery,
            // so a booking isn't held up. Azure refusing it (bad sender, rate limit) throws.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await Client.SendAsync(WaitUntil.Started, message, timeout.Token);
            return true;
        }
        catch (Exception e)
        {
            // Type only: the SDK's messages can echo the endpoint.
            log.LogWarning("Email to a guest failed ({Type}).", e.GetType().Name);
            return false;
        }
    }
}
