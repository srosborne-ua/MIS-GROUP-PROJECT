using System.Net;
using System.Net.Mail;

namespace API.Services;

// Sends plain-text-plus-HTML email over SMTP (port 587, STARTTLS). Everything comes from
// environment variables; with none set, IsConfigured is false and Send does nothing, so the
// site keeps working without email. Credentials are never logged.
//   SMTP_HOST, SMTP_PORT (default 587), SMTP_USER, SMTP_PASSWORD, EMAIL_FROM, SITE_URL
public class EmailSender(ILogger<EmailSender> log)
{
    private static string? Env(string name) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ? null : Environment.GetEnvironmentVariable(name)!.Trim();

    public static bool IsConfigured => Env("SMTP_HOST") != null && Env("EMAIL_FROM") != null;

    // The public address of the site, for links in emails. Never taken from the request.
    public static string SiteUrl => (Env("SITE_URL") ?? "http://localhost:5269").TrimEnd('/');

    // True only when the mail server accepted the message.
    public async Task<bool> SendAsync(string to, string subject, string text, string html)
    {
        if (!IsConfigured) return false;
        try
        {
            using var message = new MailMessage { From = new MailAddress(Env("EMAIL_FROM")!), Subject = subject };
            message.To.Add(to);
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(text, null, "text/plain"));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, null, "text/html"));
            using var client = new SmtpClient(Env("SMTP_HOST")!, int.TryParse(Env("SMTP_PORT"), out var port) ? port : 587)
            {
                EnableSsl = true,
                Timeout = 15000
            };
            if (Env("SMTP_USER") is { } user)
                client.Credentials = new NetworkCredential(user, Env("SMTP_PASSWORD") ?? "");
            await client.SendMailAsync(message);
            return true;
        }
        catch (Exception e)
        {
            // Type only: the message can echo server details.
            log.LogWarning("Email to a guest failed ({Type}).", e.GetType().Name);
            return false;
        }
    }
}
