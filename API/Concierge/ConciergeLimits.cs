using API.Services;

namespace API.Concierge;

public enum ConciergeBlock { None, Busy, Hourly, Daily }

// Spend protection for the concierge, on top of the per-minute rate limit in Program.cs:
// replies in progress per visitor, messages per visitor per hour, and messages for the
// whole site per day. Counts live in memory, so the app must stay at one instance
// (a restart resets them).
public class ConciergeLimits
{
    public const int PerIpInFlight = 2;
    public const int PerIpPerHour = 60;
    private const int DefaultDailyLimit = 1500;
    private const int PruneAbove = 5000;  // tracked visitors before idle ones are dropped

    // CONCIERGE_DAILY_LIMIT overrides the site-wide daily ceiling.
    public static readonly int DailyLimit =
        int.TryParse(Environment.GetEnvironmentVariable("CONCIERGE_DAILY_LIMIT"), out var n) && n > 0 ? n : DefaultDailyLimit;

    private sealed class Visitor
    {
        public DateTime HourStart;
        public int Messages;
        public int InFlight;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Visitor> _visitors = new();
    private DateOnly _day;
    private int _today;

    // Counts the message and marks a reply in progress, or says which limit was hit.
    // A visitor who got None must call Leave when the reply ends.
    public ConciergeBlock TryEnter(string visitor, DateTime? utcNow = null, DateOnly? hotelToday = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var today = hotelToday ?? HotelStore.Today();
        lock (_gate)
        {
            if (_day != today) { _day = today; _today = 0; }
            if (_visitors.Count > PruneAbove) Prune(now);

            if (!_visitors.TryGetValue(visitor, out var v)) _visitors[visitor] = v = new Visitor { HourStart = now };
            if (now - v.HourStart >= TimeSpan.FromHours(1)) { v.HourStart = now; v.Messages = 0; }

            if (v.InFlight >= PerIpInFlight) return ConciergeBlock.Busy;
            if (v.Messages >= PerIpPerHour) return ConciergeBlock.Hourly;
            if (_today >= DailyLimit) return ConciergeBlock.Daily;

            v.Messages++;
            v.InFlight++;
            _today++;
            return ConciergeBlock.None;
        }
    }

    public void Leave(string visitor)
    {
        lock (_gate)
        {
            if (_visitors.TryGetValue(visitor, out var v) && v.InFlight > 0) v.InFlight--;
        }
    }

    private void Prune(DateTime now)
    {
        foreach (var key in _visitors.Where(p => p.Value.InFlight == 0 && now - p.Value.HourStart >= TimeSpan.FromHours(1))
                                      .Select(p => p.Key).ToList())
            _visitors.Remove(key);
    }

    public static string Message(ConciergeBlock block) => block switch
    {
        ConciergeBlock.Busy => "One moment, I’m still answering your last message.",
        ConciergeBlock.Hourly => "That’s a lot of messages in a short time. Please try again in a little while, or call the front desk.",
        _ => "The concierge has reached its limit for today. You can still search and book on the site, or call the front desk."
    };
}
