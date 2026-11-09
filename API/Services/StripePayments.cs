using Stripe;
using Stripe.Checkout;
using API.Models;

namespace API.Services;

// Online payment through Stripe's hosted Checkout page. The guest types their card on
// Stripe's site, so card details never reach this server, the database, or the logs.
// Turned on by STRIPE_SECRET_KEY; without it the site keeps its prototype card form.
//   STRIPE_SECRET_KEY      sk_test_... or sk_live_...
//   STRIPE_WEBHOOK_SECRET  whsec_... (verifies Stripe's calls to POST api/payments/webhook)
public class StripePayments(ILogger<StripePayments> log)
{
    private static string? Env(string name) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ? null : Environment.GetEnvironmentVariable(name)!.Trim();

    public static bool IsConfigured => Env("STRIPE_SECRET_KEY") != null;
    // Stripe Tax adds sales/lodging tax at checkout. Turn on only after Stripe Tax is set up in the
    // dashboard (business address, registrations); otherwise creating a session fails.
    public static bool TaxEnabled => string.Equals(Env("STRIPE_TAX"), "true", StringComparison.OrdinalIgnoreCase);
    public static string? WebhookSecret => Env("STRIPE_WEBHOOK_SECRET");

    public const string ReservationKey = "reservation_id";

    private static StripeClient Client => new(Env("STRIPE_SECRET_KEY"));

    public static long Cents(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    // Starts a Checkout Session for the reservation's total, as stored in our database,
    // and returns the Stripe page to send the guest to plus the session's id.
    public async Task<(string Url, string SessionId)> CreateCheckoutAsync(Reservation r, string accountEmail, string roomName)
    {
        var back = EmailSender.SiteUrl + "/#/pay?id=" + Uri.EscapeDataString(r.Id);
        var options = new SessionCreateOptions
        {
            Mode = "payment",
            CustomerEmail = accountEmail,
            SuccessUrl = back + "&session={CHECKOUT_SESSION_ID}",
            CancelUrl = back,
            ClientReferenceId = r.Id,
            // Our prices are shown before taxes, so tax is added on top (exclusive).
            AutomaticTax = TaxEnabled ? new SessionAutomaticTaxOptions { Enabled = true } : null,
            Metadata = new Dictionary<string, string> { [ReservationKey] = r.Id },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = Cents(r.Total),
                        TaxBehavior = "exclusive",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "Druid City Hotel · " + roomName + ", room " + r.Room,
                            Description = r.Checkin.ToString("MMM d") + " to " + r.Checkout.ToString("MMM d, yyyy") + " · reservation " + r.Id
                        }
                    }
                }
            ]
        };
        var session = await new SessionService(Client).CreateAsync(options);
        return (session.Url, session.Id);
    }

    // A Checkout Session that Stripe says is paid, or null. Asked of Stripe directly, so the
    // guest returning from Stripe can't just claim it.
    public async Task<(string ReservationId, long AmountCents)?> GetPaidAsync(string sessionId)
    {
        try
        {
            var session = await new SessionService(Client).GetAsync(sessionId);
            if (session.PaymentStatus != "paid") return null;
            if (session.Metadata == null || !session.Metadata.TryGetValue(ReservationKey, out var id)) return null;
            // The amount before tax: that is what our reservation total is.
            return (id, session.AmountSubtotal ?? 0);
        }
        catch (StripeException e)
        {
            log.LogWarning("Stripe lookup failed ({Code}).", e.StripeError?.Code);
            return null;
        }
    }
}
