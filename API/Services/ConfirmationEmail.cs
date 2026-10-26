using System.Globalization;
using System.Net;
using System.Text;
using API.Models;
using Azure;
using Azure.Communication.Email;

namespace API.Services;

// Emails the guest a receipt once a booking is saved: confirmation code, stay, nightly
// rates, total, and payment status. Sent from a no-reply address through Azure
// Communication Services (ACS_EMAIL_CONNECTION and EMAIL_SENDER in .env). If either is
// unset, nothing is sent. The reservation's log in the Staff Portal records what happened.
// There are no card details to include: the server never receives them.
public class ConfirmationEmail(HotelStore store, ILogger<ConfirmationEmail> logger)
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    private readonly string? sender = Environment.GetEnvironmentVariable("EMAIL_SENDER");
    private readonly EmailClient? client = Environment.GetEnvironmentVariable("ACS_EMAIL_CONNECTION") is { Length: > 0 } connection
        ? new EmailClient(connection)
        : null;

    // Never throws: the booking stands whether or not the email goes out.
    public async Task SendAsync(Reservation r)
    {
        string log;
        if (client is null || string.IsNullOrWhiteSpace(sender))
        {
            log = "Confirmation email not sent: email isn’t set up on this server.";
        }
        else
        {
            try
            {
                var types = await store.GetRoomTypesAsync();
                var roomName = types.TryGetValue(r.Type, out var type) ? type.Name : r.Type;
                var content = new EmailContent("Your Druid City Hotel reservation, " + r.Id)
                {
                    PlainText = PlainText(r, roomName),
                    Html = Html(r, roomName)
                };
                // Started: returns once Azure accepts the message, without waiting for delivery.
                await client.SendAsync(WaitUntil.Started, new EmailMessage(sender, r.Guest.Email, content));
                log = "Confirmation emailed to " + r.Guest.Email + ".";
            }
            catch (Exception ex)
            {
                // Reservation id only; guest details stay out of the server logs.
                logger.LogWarning(ex, "Confirmation email for {Id} failed.", r.Id);
                log = "Confirmation email to " + r.Guest.Email + " couldn’t be sent. Share the details with the guest another way.";
            }
        }

        try { await store.LogSystemMessageAsync(r.Id, log); }
        catch (Exception ex) { logger.LogWarning(ex, "Couldn’t log the confirmation email for {Id}.", r.Id); }
    }

    /* ---------- Content ---------- */

    private static string Money(decimal amount) => amount.ToString("C", Us);

    private static string Day(DateOnly date) => date.ToString("dddd, MMMM d, yyyy", Us);

    private static string NightLabel(NightRate n) =>
        n.Kind == "special" && !string.IsNullOrWhiteSpace(n.Label) ? n.Label : n.Kind == "weekend" ? "Weekend night" : "Weeknight";

    private static string Payment(Reservation r) =>
        (r.PaymentStatus == "paid" ? "Paid in full" : "Due at check-in") + (r.CardOnFile ? " · Card kept on file" : "");

    private static List<(string Label, string Value)> Details(Reservation r, string roomName) =>
    [
        ("Room", roomName + ", room " + r.Room),
        ("Arrive", Day(r.Checkin) + ", from 3 PM"),
        ("Depart", Day(r.Checkout) + ", by 11 AM"),
        ("Guests", r.Guests.ToString(Us)),
        ("Payment", Payment(r))
    ];

    private const string Footer =
        "This is an automated message from a no-reply address, so replies aren’t read. " +
        "To change or cancel your stay, call the front desk with your confirmation code.";

    private static string PlainText(Reservation r, string roomName)
    {
        var text = new StringBuilder();
        text.AppendLine("Thank you, " + r.Guest.FirstName + ". Your room is held and waiting.");
        text.AppendLine();
        text.AppendLine("Confirmation: " + r.Id);
        foreach (var (label, value) in Details(r, roomName)) text.AppendLine(label + ": " + value);
        if (!string.IsNullOrWhiteSpace(r.Guest.Requests)) text.AppendLine("Requests: " + r.Guest.Requests);
        text.AppendLine();
        text.AppendLine("Receipt");
        foreach (var n in r.Nights) text.AppendLine(n.Date.ToString("ddd, MMM d", Us) + "  " + NightLabel(n) + "  " + Money(n.Amount));
        text.AppendLine("Total  " + Money(r.Total));
        text.AppendLine();
        text.AppendLine("Druid City Hotel · Tuscaloosa, Alabama");
        text.AppendLine(Footer);
        return text.ToString();
    }

    // Email clients ignore stylesheets and web fonts, so styles are inline and the
    // serif falls back to Georgia. Colors are the site's tokens (index.css :root).
    private static string Html(Reservation r, string roomName)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        const string ink = "#1c1b19", inkSoft = "#57524a", line = "#d9d3c7", accent = "#7a2e36", paper = "#f7f5f0";
        const string serif = "'Cormorant Garamond', Georgia, 'Times New Roman', serif";
        const string sans = "Inter, -apple-system, 'Segoe UI', Roboto, Arial, sans-serif";
        var cell = $"padding:10px 0;border-top:1px solid {line};font:15px/1.5 {sans};color:{ink};";
        var labelCell = $"padding:10px 16px 10px 0;border-top:1px solid {line};font:13px/1.5 {sans};color:{inkSoft};vertical-align:top;";

        var details = new StringBuilder();
        foreach (var (label, value) in Details(r, roomName))
            details.Append($"<tr><td style=\"{labelCell}\">{E(label)}</td><td style=\"{cell}\">{E(value)}</td></tr>");
        if (!string.IsNullOrWhiteSpace(r.Guest.Requests))
            details.Append($"<tr><td style=\"{labelCell}\">Requests</td><td style=\"{cell}\">{E(r.Guest.Requests)}</td></tr>");

        var nights = new StringBuilder();
        foreach (var n in r.Nights)
            nights.Append($"<tr><td style=\"{labelCell}\">{E(n.Date.ToString("ddd, MMM d", Us))}</td>" +
                          $"<td style=\"{cell}\">{E(NightLabel(n))}</td>" +
                          $"<td style=\"{cell}text-align:right;\">{E(Money(n.Amount))}</td></tr>");

        return $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{E(r.Id)}</title></head>
            <body style="margin:0;padding:0;background:{paper};">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{paper};">
            <tr><td align="center" style="padding:40px 20px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;">
                <tr><td style="font:600 13px/1.4 {sans};letter-spacing:0.12em;text-transform:uppercase;color:{accent};padding-bottom:24px;">Druid City Hotel</td></tr>
                <tr><td style="font:400 34px/1.15 {serif};color:{ink};padding-bottom:12px;">Thank you, {E(r.Guest.FirstName)}.</td></tr>
                <tr><td style="font:16px/1.6 {sans};color:{ink};padding-bottom:28px;">Your room is held and waiting. Your confirmation code is <strong>{E(r.Id)}</strong>.</td></tr>
                <tr><td><table role="presentation" width="100%" cellpadding="0" cellspacing="0">{details}</table></td></tr>
                <tr><td style="font:400 24px/1.2 {serif};color:{ink};padding:36px 0 8px;">Receipt</td></tr>
                <tr><td><table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                    {nights}
                    <tr><td style="{labelCell}border-top:1px solid {ink};"></td>
                        <td style="{cell}border-top:1px solid {ink};font-weight:600;">Total</td>
                        <td style="{cell}border-top:1px solid {ink};font-weight:600;text-align:right;">{E(Money(r.Total))}</td></tr>
                </table></td></tr>
                <tr><td style="font:13px/1.6 {sans};color:{inkSoft};padding-top:36px;border-bottom:1px solid {line};padding-bottom:20px;">Arrive from 3 PM. Depart by 11 AM. Running late? We’ll wait up.</td></tr>
                <tr><td style="font:12px/1.6 {sans};color:{inkSoft};padding-top:20px;">Druid City Hotel · Tuscaloosa, Alabama<br>{E(Footer)}</td></tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }
}
