using System.Globalization;
using System.Text.Json;
using API.Models;

namespace API.Services;

// Thrown for anything the guest or staff member can fix. The message is shown
// to them as-is, so keep it in the same voice as the rest of the site.
public class HotelRuleException(string message, bool notFound = false) : Exception(message)
{
    public bool NotFound { get; } = notFound;
}

// In-memory hotel data, seeded with the same sample data the client used to keep
// in localStorage. Registered as a singleton; everything resets when the API
// restarts. Swap for a database later without changing the controllers.
public class HotelStore
{
    private const int Floors = 3;
    private const int RoomsPerFloor = 40;
    private const int MaxOnlineNights = 30;
    private static readonly int[] SuiteNumbers = [339, 340];

    private static readonly Rates DefaultRates = new() { Weekday = 100, Weekend = 150, SuiteWeekday = 100, SuiteWeekend = 150 };

    public static readonly Dictionary<string, RoomType> RoomTypes = new()
    {
        ["king"] = new("The King", "king", "One king bed", 2, "340 sq ft",
            "A generous king, a writing desk by the window, and a rain shower built for lingering."),
        ["queen"] = new("The Double Queen", "queen", "Two queen beds", 4, "380 sq ft",
            "Room for the whole party: two queens, a lounge chair, and space to spread out on game weekend."),
        ["twin"] = new("The Twin", "twin", "Two twin beds", 2, "300 sq ft",
            "Two crisply made twins for colleagues, siblings, or old roommates back for homecoming."),
        ["suite"] = new("The Master Suite", "king", "One king bed and a separate parlor", 4, "850 sq ft",
            "Just two of them, on the top floor: a parlor for entertaining, a soaking tub, and a view over the oaks.")
    };

    private static readonly JsonSerializerOptions CopyOptions = new(JsonSerializerDefaults.Web);

    private readonly Lock _sync = new();
    private State _state = Seed();

    private class State
    {
        public Rates Rates = new();
        public List<RateOverride> RateOverrides = [];
        public List<Room> Rooms = [];
        public List<Reservation> Reservations = [];
        public List<Hold> Holds = [];
        public List<Block> Blocks = [];
        public int NextId = 1042;
        public int NextHoldId = 1;
        public int NextBlockId = 1;
    }

    private record Commitment(string Kind, Reservation? Reservation = null, Hold? Hold = null, Block? Block = null);

    /* ---------- Rooms and rates ---------- */

    public List<Room> GetRooms()
    {
        lock (_sync) return Copy(_state.Rooms);
    }

    public RatesResponse GetRates()
    {
        lock (_sync) return Copy(new RatesResponse(_state.Rates, _state.RateOverrides));
    }

    public Rates SaveRates(SaveRatesRequest input)
    {
        decimal?[] values = [input.Weekday, input.Weekend, input.SuiteWeekday, input.SuiteWeekend];
        if (values.Any(v => v is not > 0)) throw new HotelRuleException("Every rate must be a positive number.");
        static decimal Round(decimal? v) => Math.Round(v!.Value, 2, MidpointRounding.AwayFromZero);
        lock (_sync)
        {
            _state.Rates = new Rates
            {
                Weekday = Round(input.Weekday),
                Weekend = Round(input.Weekend),
                SuiteWeekday = Round(input.SuiteWeekday),
                SuiteWeekend = Round(input.SuiteWeekend)
            };
            return Copy(_state.Rates);
        }
    }

    public List<RateOverride> AddRateOverride(RateOverrideRequest input)
    {
        var date = ParseDate(input.Date) ?? throw new HotelRuleException("Choose a date.");
        if (input.Amount is not > 0) throw new HotelRuleException("Enter a positive rate.");
        var label = string.IsNullOrWhiteSpace(input.Label) ? "Special rate" : input.Label.Trim();
        lock (_sync)
        {
            _state.RateOverrides.RemoveAll(o => o.Date == date);
            _state.RateOverrides.Add(new RateOverride(date, input.Amount.Value, label));
            _state.RateOverrides.Sort((a, b) => a.Date.CompareTo(b.Date));
            return Copy(_state.RateOverrides);
        }
    }

    public List<RateOverride> RemoveRateOverride(DateOnly date)
    {
        lock (_sync)
        {
            _state.RateOverrides.RemoveAll(o => o.Date == date);
            return Copy(_state.RateOverrides);
        }
    }

    public List<RatePreviewDay> GetRatePreview(DateOnly start, int days)
    {
        lock (_sync)
        {
            return Enumerable.Range(0, Math.Clamp(days, 0, 366))
                .Select(i => start.AddDays(i))
                .Select(d => new RatePreviewDay(d, NightlyRate("king", d, _state), NightlyRate("suite", d, _state)))
                .ToList();
        }
    }

    // Available rooms for a date range, grouped by room type with the total cost.
    public List<AvailabilityGroup> SearchAvailability(AvailabilityQuery query)
    {
        var (checkin, checkout) = CheckStayDates(query.Checkin, query.Checkout, query.AllowPast);
        var guests = query.Guests is > 0 ? query.Guests.Value : 1;
        int? floor = int.TryParse(query.Floor, out var f) ? f : null;
        var bed = string.IsNullOrEmpty(query.Bed) || query.Bed == "any" ? null : query.Bed;

        lock (_sync)
        {
            var open = _state.Rooms.Where(room =>
            {
                var type = RoomTypes[room.Type];
                if (bed != null && type.Bed != bed) return false;
                if (floor != null && room.Floor != floor) return false;
                if (type.Sleeps < guests) return false;
                return CommitmentsFor(room.Number, checkin, checkout).Count == 0;
            }).ToList();

            return RoomTypes.Keys
                .Select(type => (type, rooms: open.Where(r => r.Type == type).ToList()))
                .Where(g => g.rooms.Count > 0)
                .Select(g => new AvailabilityGroup(
                    g.type,
                    g.rooms.Select(r => new RoomSummary(r.Number, r.Floor)).ToList(),
                    PriceStay(g.type, checkin, checkout, _state)))
                .ToList();
        }
    }

    public RoomCheck CheckRoom(int number, string? checkinText, string? checkoutText)
    {
        var checkin = ParseDate(checkinText);
        var checkout = ParseDate(checkoutText);
        if (checkin == null || checkout == null) throw new HotelRuleException("Choose valid dates.");
        lock (_sync)
        {
            var room = _state.Rooms.Find(r => r.Number == number)
                ?? throw new HotelRuleException("We couldn’t find that room.", notFound: true);
            return new RoomCheck(room,
                CommitmentsFor(room.Number, checkin.Value, checkout.Value).Count == 0,
                PriceStay(room.Type, checkin.Value, checkout.Value, _state));
        }
    }

    // Status of every room for a single night.
    public List<RoomBoardEntry> GetRoomBoard(DateOnly date)
    {
        lock (_sync)
        {
            return _state.Rooms.Select(room =>
            {
                var c = CommitmentsFor(room.Number, date, date.AddDays(1));
                var top = c.Find(x => x.Kind == "reservation") ?? c.Find(x => x.Kind == "block") ?? c.Find(x => x.Kind == "hold");
                return new RoomBoardEntry(room.Number, room.Floor, room.Type,
                    top?.Kind ?? "available", top == null ? "" : Describe(top));
            }).ToList();
        }
    }

    // Upcoming reservations, holds, and blocks for one room.
    public List<ScheduleItem> GetRoomSchedule(int number)
    {
        var t = Today();
        lock (_sync)
        {
            var items = new List<ScheduleItem>();
            items.AddRange(_state.Reservations
                .Where(r => r.Room == number && r.Status == "confirmed" && r.Checkout > t)
                .Select(r => new ScheduleItem("reservation", r.Id, r.Checkin, r.Checkout.AddDays(-1), r.Guest.FirstName + " " + r.Guest.LastName)));
            items.AddRange(_state.Holds
                .Where(h => h.Room == number && h.End >= t)
                .Select(h => new ScheduleItem("hold", h.Id, h.Start, h.End, h.Reason)));
            items.AddRange(_state.Blocks
                .Where(b => b.Rooms.Contains(number) && b.End >= t)
                .Select(b => new ScheduleItem("block", b.Id, b.Start, b.End, b.Name)));
            return items.OrderBy(i => i.Start).ToList();
        }
    }

    /* ---------- Reservations ---------- */

    public List<Reservation> GetReservations()
    {
        lock (_sync) return Copy(_state.Reservations);
    }

    public Reservation GetReservation(string id)
    {
        lock (_sync) return Copy(FindReservation(id, "We couldn’t find reservation " + id + "."));
    }

    public Reservation CreateReservation(CreateReservationRequest input)
    {
        var isStaff = input.Source == "staff";
        var g = input.Guest ?? new Guest();
        lock (_sync)
        {
            var room = _state.Rooms.Find(r => r.Number == input.Room) ?? throw new HotelRuleException("Choose a room.");
            var (checkin, checkout) = CheckStayDates(input.Checkin, input.Checkout, allowPast: isStaff);
            var guests = input.Guests is > 0 ? input.Guests.Value : 1;
            var sleeps = RoomTypes[room.Type].Sleeps;
            if (guests > sleeps) throw new HotelRuleException("That room sleeps " + sleeps + ".");
            if (CommitmentsFor(room.Number, checkin, checkout).Count > 0)
                throw new HotelRuleException("Room " + room.Number + " is no longer open for those dates.");

            var quote = PriceStay(room.Type, checkin, checkout, _state);
            var now = DateTime.UtcNow;
            var email = (g.Email ?? "").Trim();
            var reminder = checkin.AddDays(-2) > Today() ? checkin.AddDays(-2) : Today();
            var reservation = new Reservation
            {
                Id = "DC-" + _state.NextId++,
                Room = room.Number,
                Type = room.Type,
                Checkin = checkin,
                Checkout = checkout,
                Guests = guests,
                Guest = new Guest
                {
                    FirstName = (g.FirstName ?? "").Trim(),
                    LastName = (g.LastName ?? "").Trim(),
                    Email = email,
                    Phone = (g.Phone ?? "").Trim(),
                    Requests = (g.Requests ?? "").Trim()
                },
                Nights = quote.Nights,
                Total = quote.Total,
                PaymentStatus = input.PaymentStatus == "paid" ? "paid" : "unpaid",
                CardOnFile = input.CardOnFile,
                Source = isStaff ? "staff" : "web",
                Status = "confirmed",
                CreatedAt = now,
                Messages =
                [
                    new(now, "system", "Confirmation emailed to " + email + "."),
                    new(now, "system", "Pre-arrival note (parking, directions, check-in time) scheduled for " + FormatDate(reminder) + ".")
                ]
            };
            _state.Reservations.Add(reservation);
            return Copy(reservation);
        }
    }

    public Reservation CancelReservation(string id)
    {
        lock (_sync)
        {
            var r = FindReservation(id, "Reservation not found.");
            r.Status = "cancelled";
            r.Messages.Add(new(DateTime.UtcNow, "system", "Reservation cancelled. Cancellation notice emailed to guest."));
            return Copy(r);
        }
    }

    public Reservation SetPaymentStatus(string id, string? status)
    {
        lock (_sync)
        {
            var r = FindReservation(id, "Reservation not found.");
            r.PaymentStatus = status == "paid" ? "paid" : "unpaid";
            return Copy(r);
        }
    }

    public Reservation SendGuestMessage(string id, string? text)
    {
        lock (_sync)
        {
            var r = FindReservation(id, "Reservation not found.");
            if (string.IsNullOrWhiteSpace(text)) throw new HotelRuleException("Write a message first.");
            r.Messages.Add(new(DateTime.UtcNow, "staff", text.Trim()));
            return Copy(r);
        }
    }

    /* ---------- Holds and event blocks ---------- */

    public List<Hold> GetHolds()
    {
        var t = Today();
        lock (_sync) return Copy(_state.Holds.Where(h => h.End >= t).OrderBy(h => h.Start).ToList());
    }

    public Hold CreateHold(CreateHoldRequest input)
    {
        lock (_sync)
        {
            if (input.Room is not { } room || !_state.Rooms.Exists(r => r.Number == room)) throw new HotelRuleException("Choose a room.");
            var (start, end) = CheckRange(input.Start, input.End);
            if (string.IsNullOrWhiteSpace(input.Reason)) throw new HotelRuleException("Add a reason so the team knows why.");
            var conflicts = CommitmentsFor(room, start, end.AddDays(1));
            if (conflicts.Count > 0)
                throw new HotelRuleException("Room " + room + " is already taken: " + string.Join("; ", conflicts.Select(Describe)) + ".");
            var hold = new Hold("H-" + _state.NextHoldId++, room, start, end, input.Reason.Trim(), DateTime.UtcNow);
            _state.Holds.Add(hold);
            return Copy(hold);
        }
    }

    public void ReleaseHold(string id)
    {
        lock (_sync) _state.Holds.RemoveAll(h => h.Id == id);
    }

    public List<Block> GetBlocks()
    {
        var t = Today();
        lock (_sync) return Copy(_state.Blocks.Where(b => b.End >= t).OrderBy(b => b.Start).ToList());
    }

    public Block CreateBlock(CreateBlockRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new HotelRuleException("Name the event.");
        var (start, end) = CheckRange(input.Start, input.End);
        if (input.Rooms is not { Count: > 0 }) throw new HotelRuleException("List at least one room.");
        lock (_sync)
        {
            var unknown = input.Rooms.Where(n => !_state.Rooms.Exists(r => r.Number == n)).ToList();
            if (unknown.Count > 0) throw new HotelRuleException("These aren’t room numbers: " + string.Join(", ", unknown) + ".");
            var taken = input.Rooms
                .Select(n => (n, c: CommitmentsFor(n, start, end.AddDays(1))))
                .Where(x => x.c.Count > 0)
                .ToList();
            if (taken.Count > 0)
            {
                throw new HotelRuleException("These rooms are already taken on those nights: " +
                    string.Join(", ", taken.Select(x => x.n + " (" + Describe(x.c[0]) + ")")) + ".");
            }
            var block = new Block("B-" + _state.NextBlockId++, input.Name.Trim(), start, end, input.Rooms.Distinct().ToList(), DateTime.UtcNow);
            _state.Blocks.Add(block);
            return Copy(block);
        }
    }

    public void RemoveBlock(string id)
    {
        lock (_sync) _state.Blocks.RemoveAll(b => b.Id == id);
    }

    public void ResetDemoData()
    {
        lock (_sync) _state = Seed();
    }

    /* ---------- Rules ---------- */

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    // Matches fmtDate() in the client: "Fri, Oct 16, 2026".
    private static string FormatDate(DateOnly date) => date.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture);

    // Same rules and wording as checkStayDates() in the client.
    private static (DateOnly Checkin, DateOnly Checkout) CheckStayDates(string? checkinText, string? checkoutText, bool allowPast)
    {
        var checkin = ParseDate(checkinText) ?? throw new HotelRuleException("Choose an arrival date.");
        var checkout = ParseDate(checkoutText) ?? throw new HotelRuleException("Choose a departure date.");
        if (!allowPast && checkin < Today()) throw new HotelRuleException("Arrival can’t be in the past.");
        if (checkout <= checkin) throw new HotelRuleException("Departure must be after arrival.");
        if (checkout.DayNumber - checkin.DayNumber > MaxOnlineNights)
            throw new HotelRuleException("Online stays are limited to 30 nights. Call us for longer visits.");
        return (checkin, checkout);
    }

    // First and last night (inclusive) for holds and blocks.
    private static (DateOnly Start, DateOnly End) CheckRange(string? startText, string? endText)
    {
        var start = ParseDate(startText);
        var end = ParseDate(endText);
        if (start == null || end == null) throw new HotelRuleException("Choose the first and last night.");
        if (end < start) throw new HotelRuleException("The last night can’t be before the first.");
        return (start.Value, end.Value);
    }

    // Friday and Saturday nights are weekend nights.
    private static bool IsWeekendNight(DateOnly date) => date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;

    private static NightRate NightlyRate(string type, DateOnly date, State state)
    {
        var special = state.RateOverrides.Find(o => o.Date == date);
        if (special != null) return new NightRate(date, special.Amount, "special", special.Label);
        var weekend = IsWeekendNight(date);
        var amount = type == "suite"
            ? (weekend ? state.Rates.SuiteWeekend : state.Rates.SuiteWeekday)
            : (weekend ? state.Rates.Weekend : state.Rates.Weekday);
        return new NightRate(date, amount, weekend ? "weekend" : "weekday");
    }

    private static Quote PriceStay(string type, DateOnly checkin, DateOnly checkout, State state)
    {
        var nights = new List<NightRate>();
        for (var d = checkin; d < checkout; d = d.AddDays(1)) nights.Add(NightlyRate(type, d, state));
        return new Quote(nights, nights.Sum(n => n.Amount));
    }

    // Everything that occupies a room for any night in [checkin, checkout).
    private List<Commitment> CommitmentsFor(int room, DateOnly checkin, DateOnly checkout)
    {
        static bool Overlaps(DateOnly aStart, DateOnly aEndExcl, DateOnly bStart, DateOnly bEndExcl) =>
            aStart < bEndExcl && bStart < aEndExcl;

        var found = new List<Commitment>();
        found.AddRange(_state.Reservations
            .Where(r => r.Status == "confirmed" && r.Room == room && Overlaps(r.Checkin, r.Checkout, checkin, checkout))
            .Select(r => new Commitment("reservation", Reservation: r)));
        found.AddRange(_state.Holds
            .Where(h => h.Room == room && Overlaps(h.Start, h.End.AddDays(1), checkin, checkout))
            .Select(h => new Commitment("hold", Hold: h)));
        found.AddRange(_state.Blocks
            .Where(b => b.Rooms.Contains(room) && Overlaps(b.Start, b.End.AddDays(1), checkin, checkout))
            .Select(b => new Commitment("block", Block: b)));
        return found;
    }

    private static string Describe(Commitment c) => c.Kind switch
    {
        "reservation" => c.Reservation!.Id + " · " + c.Reservation.Guest.FirstName + " " + c.Reservation.Guest.LastName,
        "hold" => "Hold · " + c.Hold!.Reason,
        _ => "Event · " + c.Block!.Name
    };

    private Reservation FindReservation(string id, string notFoundMessage) =>
        _state.Reservations.Find(r => r.Id == id) ?? throw new HotelRuleException(notFoundMessage, notFound: true);

    // Callers get a snapshot, never a live reference into the store.
    private static T Copy<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, CopyOptions), CopyOptions)!;

    /* ---------- Sample data ---------- */

    private static List<Room> BuildRooms()
    {
        // 40 rooms per floor. Every third room is a king (~1/3 of the hotel);
        // the rest alternate queen and twin. 339 and 340 are the master suites.
        var rooms = new List<Room>();
        for (var floor = 1; floor <= Floors; floor++)
        {
            for (var i = 1; i <= RoomsPerFloor; i++)
            {
                var number = floor * 100 + i;
                var type = SuiteNumbers.Contains(number) ? "suite"
                    : i % 3 == 0 ? "king"
                    : i % 2 == 0 ? "queen"
                    : "twin";
                rooms.Add(new Room(number, floor, type));
            }
        }
        return rooms;
    }

    private static State Seed()
    {
        var t = Today();
        var state = new State
        {
            Rates = new Rates
            {
                Weekday = DefaultRates.Weekday,
                Weekend = DefaultRates.Weekend,
                SuiteWeekday = DefaultRates.SuiteWeekday,
                SuiteWeekend = DefaultRates.SuiteWeekend
            },
            Rooms = BuildRooms()
        };

        // first, last, room, arrive offset, nights, guests, payment, card on file, requests, status
        (string First, string Last, int Room, int Offset, int Nights, int Guests, string Payment, bool CardOnFile, string Requests, string Status)[] seeds =
        [
            ("Eleanor", "Whitfield", 104, -1, 3, 2, "paid", true, "Late arrival, around 10 PM.", "confirmed"),
            ("Marcus", "Bell", 212, 0, 3, 1, "unpaid", false, "", "confirmed"),
            ("Priya", "Natarajan", 339, 3, 3, 2, "paid", true, "Celebrating our anniversary.", "confirmed"),
            ("Thomas", "Greer", 127, 1, 1, 1, "unpaid", false, "", "confirmed"),
            ("Ruth", "Abernathy", 232, 5, 2, 3, "paid", false, "Quiet room away from the elevator, please.", "confirmed"),
            ("Daniel", "Okafor", 315, 7, 3, 2, "unpaid", false, "", "confirmed"),
            ("Caroline", "Hollis", 340, 12, 2, 4, "paid", true, "", "confirmed"),
            ("Samuel", "Price", 109, 2, 2, 1, "paid", false, "Need a receipt for work.", "confirmed"),
            ("Grace", "Lee", 205, 4, 2, 2, "unpaid", false, "", "cancelled"),
            ("Henry", "Castellanos", 136, -6, 2, 2, "paid", false, "", "confirmed")
        ];

        for (var i = 0; i < seeds.Length; i++)
        {
            var s = seeds[i];
            var checkin = t.AddDays(s.Offset);
            var checkout = checkin.AddDays(s.Nights);
            var type = state.Rooms.Find(r => r.Number == s.Room)!.Type;
            var quote = PriceStay(type, checkin, checkout, state);
            var created = DateTime.UtcNow.AddDays(-(14 - i));
            var email = s.First.ToLowerInvariant() + "." + s.Last.ToLowerInvariant() + "@example.com";
            state.Reservations.Add(new Reservation
            {
                Id = "DC-" + (1030 + i),
                Room = s.Room,
                Type = type,
                Checkin = checkin,
                Checkout = checkout,
                Guests = s.Guests,
                Guest = new Guest { FirstName = s.First, LastName = s.Last, Email = email, Phone = "(205) 555-01" + (10 + i), Requests = s.Requests },
                Nights = quote.Nights,
                Total = quote.Total,
                PaymentStatus = s.Payment,
                CardOnFile = s.CardOnFile,
                Source = i % 3 == 0 ? "staff" : "web",
                Status = s.Status,
                CreatedAt = created,
                Messages = [new(created, "system", "Confirmation emailed to " + email + ".")]
            });
        }

        state.Holds.Add(new Hold("H-" + state.NextHoldId++, 118, t, t.AddDays(2), "Maintenance: HVAC service", DateTime.UtcNow));
        state.Blocks.Add(new Block("B-" + state.NextBlockId++, "Abernathy–Hollis wedding party",
            t.AddDays(20), t.AddDays(21), Enumerable.Range(201, 10).ToList(), DateTime.UtcNow));
        return state;
    }
}
