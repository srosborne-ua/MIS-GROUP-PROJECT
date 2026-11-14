using System.Net;
using API.Models;

namespace API.Services;

// The text of every email the site sends. Guest-entered text is HTML-encoded.
public static class GuestEmails
{
    private static string H(string s) => WebUtility.HtmlEncode(s);
    private static string Money(decimal v) => v.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
    private static string Day(DateOnly d) => d.ToString("ddd, MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private const string NoReply = "This is an automated message from an unmonitored address, so please don’t reply. For help, call the front desk.";

    private static (string Text, string Html) Wrap(string heading, string[] lines, string? link = null, string? linkLabel = null)
    {
        var text = heading + "\n\n" + string.Join("\n", lines) + (link != null ? "\n\n" + linkLabel + ": " + link : "") +
                   "\n\nDruid City Hotel, Tuscaloosa, Alabama\n" + NoReply;
        var html = "<div style=\"font-family:Georgia,serif;color:#1c1b19;max-width:34rem\"><h2 style=\"font-weight:normal\">" + H(heading) + "</h2>" +
                   string.Concat(lines.Select(l => "<p style=\"font-family:Arial,sans-serif;line-height:1.5\">" + H(l) + "</p>")) +
                   (link != null ? "<p><a href=\"" + H(link) + "\" style=\"color:#8c2f39\">" + H(linkLabel ?? link) + "</a></p>" : "") +
                   "<p style=\"font-family:Arial,sans-serif;color:#6b665e;font-size:0.85rem\">Druid City Hotel, Tuscaloosa, Alabama<br>" + H(NoReply) + "</p></div>";
        return (text, html);
    }

    public static (string Subject, string Text, string Html) Confirmation(Reservation r)
    {
        var nights = r.Checkout.DayNumber - r.Checkin.DayNumber;
        var lines = new[]
        {
            "Hello " + r.Guest.FirstName + ", your stay is booked.",
            "Reservation " + r.Id + " · room " + r.Room,
            Day(r.Checkin) + " to " + Day(r.Checkout) + " (" + nights + (nights == 1 ? " night" : " nights") + ", " + r.Guests + (r.Guests == 1 ? " guest" : " guests") + ")",
            "Total: " + Money(r.Total) + " before taxes. " + (r.PaymentStatus != "paid" ? "Due at check-in." : r.PaymentMethod == "online" ? "Paid by card online." : "Paid at the hotel."),
            r.PaymentStatus != "paid"
                ? "You can cancel this stay from your account until check-in. To change it, call the front desk."
                : "To change or cancel, call the front desk."
        };
        var (t, h) = Wrap("You’re booked.", lines, EmailSender.SiteUrl + "/#/account", "See your stays");
        return ("Your Druid City Hotel reservation " + r.Id, t, h);
    }

    public static (string Subject, string Text, string Html) Cancellation(Reservation r)
    {
        var lines = new[]
        {
            "Hello " + r.Guest.FirstName + ", reservation " + r.Id + " (" + Day(r.Checkin) + " to " + Day(r.Checkout) + ") has been cancelled.",
            "If this wasn’t expected, please call the front desk."
        };
        var (t, h) = Wrap("Reservation cancelled.", lines);
        return ("Your Druid City Hotel reservation " + r.Id + " was cancelled", t, h);
    }

    public static (string Subject, string Text, string Html) PasswordReset(string firstName, string link, int minutes)
    {
        var lines = new[]
        {
            "Hello " + firstName + ", we got a request to reset your password.",
            "This link works once and expires in " + minutes + " minutes. If you didn’t ask for it, ignore this email and your password stays the same."
        };
        var (t, h) = Wrap("Reset your password.", lines, link, "Choose a new password");
        return ("Reset your Druid City Hotel password", t, h);
    }
}
