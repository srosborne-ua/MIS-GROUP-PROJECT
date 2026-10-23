using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using API.Database;
using API.Models;
using Dapper;
using MySqlConnector;

namespace API.Services;

// Thrown for anything the guest or staff member can fix. The message is shown
// to them as-is, so keep it in the same voice as the rest of the site.
public class HotelRuleException(string message, bool notFound = false) : Exception(message)
{
    public bool NotFound { get; } = notFound;
}

// Hotel data in MySQL (API/Database/schema.sql), read and written as druid_app
// with parameterized queries. Owns every booking, availability, and pricing rule.
//
// Availability is never stored: a room is open when no confirmed booking,
// maintenance block (hold), or event block overlaps the nights asked for.
//
// Double-booking: every write that takes a room (reservation, hold, event block)
// runs in a READ COMMITTED transaction that locks the room rows first
// (SELECT ... FOR UPDATE), then re-checks for overlaps, then inserts. A second
// writer for the same room waits on the lock and then sees the first one's row.
public class HotelStore(Db db)
{
    private const decimal MaxRate = 999999.99m;   // DECIMAL(8,2)
    private const string ReservationPrefix = "DC-";
    private const string HoldPrefix = "H-";
    private const string BlockPrefix = "B-";

    public static readonly Rates DefaultRates = new() { Weekday = 100, Weekend = 150, SuiteWeekday = 100, SuiteWeekend = 150 };

    // Same check as isEmail() in the client.
    private static readonly Regex EmailPattern = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    private const string RoomSelect = """
        SELECT r.id, r.room_number, r.floor, r.is_active, t.id AS room_type_id, t.code, t.sleeps
        FROM room r JOIN room_type t ON t.id = r.room_type_id
        WHERE r.hotel_id = @HotelId
        """;

    /* ---------- Rooms and rates ---------- */

    public async Task<Dictionary<string, RoomType>> GetRoomTypesAsync()
    {
        await using var conn = await db.OpenAsync();
        var types = await conn.QueryAsync<RoomTypeRow>(
            "SELECT code, name, bed, bed_label, sleeps, size_sqft, blurb FROM room_type WHERE hotel_id = @HotelId ORDER BY id",
            new { Db.HotelId });
        return types.ToDictionary(t => t.Code, t =>
            new RoomType(t.Name, t.Bed, t.BedLabel, t.Sleeps, t.SizeSqft is { } size ? size + " sq ft" : "", t.Blurb));
    }

    public async Task<List<Room>> GetRoomsAsync()
    {
        await using var conn = await db.OpenAsync();
        var rooms = await conn.QueryAsync<RoomRow>(RoomSelect + " ORDER BY r.room_number", new { Db.HotelId });
        return rooms.Select(r => new Room(r.RoomNumber, r.Floor, r.Code)).ToList();
    }

    public async Task<RatesResponse> GetRatesAsync()
    {
        await using var conn = await db.OpenAsync();
        return new RatesResponse(await LoadRatesAsync(conn), await LoadOverridesAsync(conn));
    }

    // King, Double Queen, and Twin share the standard rates; suites have their own.
    public async Task<Rates> SaveRatesAsync(SaveRatesRequest input)
    {
        decimal?[] values = [input.Weekday, input.Weekend, input.SuiteWeekday, input.SuiteWeekend];
        if (values.Any(v => v is not > 0)) throw new HotelRuleException("Every rate must be a positive number.");
        var rates = new Rates
        {
            Weekday = RoundRate(input.Weekday!.Value),
            Weekend = RoundRate(input.Weekend!.Value),
            SuiteWeekday = RoundRate(input.SuiteWeekday!.Value),
            SuiteWeekend = RoundRate(input.SuiteWeekend!.Value)
        };
        if (new[] { rates.Weekday, rates.Weekend, rates.SuiteWeekday, rates.SuiteWeekend }.Any(v => v > MaxRate))
            throw new HotelRuleException("Every rate must be under $1,000,000.");

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync(
            """
            UPDATE room_type SET weekday_rate = @Weekday, weekend_rate = @Weekend
                WHERE hotel_id = @HotelId AND code <> 'suite';
            UPDATE room_type SET weekday_rate = @SuiteWeekday, weekend_rate = @SuiteWeekend
                WHERE hotel_id = @HotelId AND code = 'suite';
            """,
            new { Db.HotelId, rates.Weekday, rates.Weekend, rates.SuiteWeekday, rates.SuiteWeekend }, tx);
        await tx.CommitAsync();
        return rates;
    }

    // A special rate applies to every room type for one night. Adding one on a night
    // that already has one replaces it.
    public async Task<List<RateOverride>> AddRateOverrideAsync(RateOverrideRequest input)
    {
        var date = ParseDate(input.Date) ?? throw new HotelRuleException("Choose a date.");
        if (input.Amount is not > 0) throw new HotelRuleException("Enter a positive rate.");
        var amount = RoundRate(input.Amount.Value);
        if (amount > MaxRate) throw new HotelRuleException("Enter a rate under $1,000,000.");
        var label = string.IsNullOrWhiteSpace(input.Label) ? "Special rate" : input.Label.Trim();
        if (label.Length > 80) throw new HotelRuleException("Keep the label under 80 characters.");

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        // One rate change at a time, so two staff members can't create overlapping rates.
        await conn.ExecuteAsync("SELECT id FROM hotel WHERE id = @HotelId FOR UPDATE", new { Db.HotelId }, tx);
        await conn.ExecuteAsync(
            """
            DELETE FROM seasonal_rate
            WHERE hotel_id = @HotelId AND room_type_id IS NULL AND first_night = @Date AND last_night = @Date
            """,
            new { Db.HotelId, Date = date }, tx);
        var covering = await conn.ExecuteScalarAsync<string?>(
            """
            SELECT label FROM seasonal_rate
            WHERE hotel_id = @HotelId AND room_type_id IS NULL AND first_night <= @Date AND last_night >= @Date
            LIMIT 1
            """,
            new { Db.HotelId, Date = date }, tx);
        if (covering != null) throw new HotelRuleException("That night is already covered by the special rate “" + covering + "”.");
        await conn.ExecuteAsync(
            """
            INSERT INTO seasonal_rate (hotel_id, room_type_id, first_night, last_night, nightly_rate, label)
            VALUES (@HotelId, NULL, @Date, @Date, @Amount, @Label)
            """,
            new { Db.HotelId, Date = date, Amount = amount, Label = label }, tx);
        await tx.CommitAsync();
        return await LoadOverridesAsync(conn);
    }

    public async Task<List<RateOverride>> RemoveRateOverrideAsync(DateOnly date)
    {
        await using var conn = await db.OpenAsync();
        await conn.ExecuteAsync(
            """
            DELETE FROM seasonal_rate
            WHERE hotel_id = @HotelId AND room_type_id IS NULL AND first_night = @Date AND last_night = @Date
            """,
            new { Db.HotelId, Date = date });
        return await LoadOverridesAsync(conn);
    }

    public async Task<List<RatePreviewDay>> GetRatePreviewAsync(DateOnly start, int days)
    {
        var count = Math.Clamp(days, 0, 366);
        await using var conn = await db.OpenAsync();
        var book = await RateBook.LoadAsync(conn, null, start, start.AddDays(count));
        return Enumerable.Range(0, count)
            .Select(i => start.AddDays(i))
            .Select(d => new RatePreviewDay(d, book.Night("king", d), book.Night("suite", d)))
            .ToList();
    }

    // Available rooms for a date range, grouped by room type with the total cost.
    public async Task<List<AvailabilityGroup>> SearchAvailabilityAsync(AvailabilityQuery query)
    {
        await using var conn = await db.OpenAsync();
        var (checkin, checkout) = CheckStayDates(query.Checkin, query.Checkout, query.AllowPast, await MaxNightsAsync(conn));
        var guests = query.Guests is > 0 ? query.Guests.Value : 1;
        int? floor = int.TryParse(query.Floor, out var f) ? f : null;
        var bed = string.IsNullOrEmpty(query.Bed) || query.Bed == "any" ? null : query.Bed;

        var open = await conn.QueryAsync<RoomRow>(RoomSelect +
            """

              AND r.is_active AND t.sleeps >= @Guests
              AND (@Bed IS NULL OR t.bed = @Bed)
              AND (@Floor IS NULL OR r.floor = @Floor)
              AND NOT EXISTS (SELECT 1 FROM booking b
                              WHERE b.room_id = r.id AND b.status = 'confirmed'
                                AND b.check_in < @Checkout AND b.check_out > @Checkin)
              AND NOT EXISTS (SELECT 1 FROM maintenance_block m
                              WHERE m.room_id = r.id
                                AND m.first_night < @Checkout AND m.last_night >= @Checkin)
              AND NOT EXISTS (SELECT 1 FROM event_block_room ebr JOIN event_block e ON e.id = ebr.event_block_id
                              WHERE ebr.room_id = r.id
                                AND e.first_night < @Checkout AND e.last_night >= @Checkin)
            ORDER BY t.id, r.room_number
            """,
            new { Db.HotelId, Guests = guests, Bed = bed, Floor = floor, Checkin = checkin, Checkout = checkout });

        var book = await RateBook.LoadAsync(conn, null, checkin, checkout);
        return open
            .GroupBy(r => r.Code)
            .Select(g => new AvailabilityGroup(
                g.Key,
                g.Select(r => new RoomSummary(r.RoomNumber, r.Floor)).ToList(),
                book.Price(g.Key, checkin, checkout)))
            .ToList();
    }

    public async Task<RoomCheck> CheckRoomAsync(int number, string? checkinText, string? checkoutText)
    {
        var checkin = ParseDate(checkinText);
        var checkout = ParseDate(checkoutText);
        if (checkin == null || checkout == null) throw new HotelRuleException("Choose valid dates.");
        await using var conn = await db.OpenAsync();
        var room = await conn.QuerySingleOrDefaultAsync<RoomRow>(RoomSelect + " AND r.room_number = @Number",
            new { Db.HotelId, Number = number })
            ?? throw new HotelRuleException("We couldn’t find that room.", notFound: true);
        var conflicts = await CommitmentsAsync(conn, null, [number], checkin.Value, checkout.Value);
        var book = await RateBook.LoadAsync(conn, null, checkin.Value, checkout.Value);
        return new RoomCheck(new Room(room.RoomNumber, room.Floor, room.Code),
            conflicts.Count == 0,
            book.Price(room.Code, checkin.Value, checkout.Value));
    }

    // Status of every room for a single night.
    public async Task<List<RoomBoardEntry>> GetRoomBoardAsync(DateOnly date)
    {
        await using var conn = await db.OpenAsync();
        var rooms = await conn.QueryAsync<RoomRow>(RoomSelect + " ORDER BY r.room_number", new { Db.HotelId });
        var commitments = (await CommitmentsAsync(conn, null, null, date, date.AddDays(1))).ToLookup(c => c.RoomNumber);
        return rooms.Select(room =>
        {
            var c = commitments[room.RoomNumber].ToList();
            var top = c.Find(x => x.Kind == "reservation") ?? c.Find(x => x.Kind == "block") ?? c.Find(x => x.Kind == "hold");
            return new RoomBoardEntry(room.RoomNumber, room.Floor, room.Code,
                top?.Kind ?? "available", top == null ? "" : Describe(top));
        }).ToList();
    }

    // Upcoming reservations, holds, and blocks for one room.
    public async Task<List<ScheduleItem>> GetRoomScheduleAsync(int number)
    {
        await using var conn = await db.OpenAsync();
        var items = await conn.QueryAsync<ScheduleRow>(
            """
            SELECT 'reservation' AS kind, b.id AS ref_id, b.check_in AS first_night,
                   DATE_SUB(b.check_out, INTERVAL 1 DAY) AS last_night,
                   CONCAT(b.guest_first_name, ' ', b.guest_last_name) AS label
            FROM booking b JOIN room r ON r.id = b.room_id
            WHERE r.hotel_id = @HotelId AND r.room_number = @Number
              AND b.status = 'confirmed' AND b.check_out > @Today
            UNION ALL
            SELECT 'hold', m.id, m.first_night, m.last_night, m.reason
            FROM maintenance_block m JOIN room r ON r.id = m.room_id
            WHERE r.hotel_id = @HotelId AND r.room_number = @Number AND m.last_night >= @Today
            UNION ALL
            SELECT 'block', e.id, e.first_night, e.last_night, e.name
            FROM event_block e
            JOIN event_block_room ebr ON ebr.event_block_id = e.id
            JOIN room r ON r.id = ebr.room_id
            WHERE r.hotel_id = @HotelId AND r.room_number = @Number AND e.last_night >= @Today
            ORDER BY first_night
            """,
            new { Db.HotelId, Number = number, Today = Today() });
        return items.Select(i => new ScheduleItem(i.Kind, PublicId(i.Kind, i.RefId),
            DateOnly.FromDateTime(i.FirstNight), DateOnly.FromDateTime(i.LastNight), i.Label)).ToList();
    }

    /* ---------- Reservations ---------- */

    public async Task<List<Reservation>> GetReservationsAsync()
    {
        await using var conn = await db.OpenAsync();
        return await LoadReservationsAsync(conn, null);
    }

    public async Task<Reservation> GetReservationAsync(string id)
    {
        var notFound = "We couldn’t find reservation " + id + ".";
        var bookingId = ParseId(ReservationPrefix, id) ?? throw new HotelRuleException(notFound, notFound: true);
        await using var conn = await db.OpenAsync();
        return (await LoadReservationsAsync(conn, bookingId)).FirstOrDefault()
            ?? throw new HotelRuleException(notFound, notFound: true);
    }

    public async Task<Reservation> CreateReservationAsync(CreateReservationRequest input)
    {
        var isStaff = input.Source == "staff";
        if (input.Room is not { } number) throw new HotelRuleException("Choose a room.");

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var room = (await LockRoomsAsync(conn, tx, [number])).FirstOrDefault(r => r.IsActive)
            ?? throw new HotelRuleException("Choose a room.");
        var (checkin, checkout) = CheckStayDates(input.Checkin, input.Checkout, allowPast: isStaff, await MaxNightsAsync(conn, tx));
        var guests = input.Guests is > 0 ? input.Guests.Value : 1;
        if (guests > room.Sleeps) throw new HotelRuleException("That room sleeps " + room.Sleeps + ".");
        var guest = CheckGuest(input.Guest);
        if ((await CommitmentsAsync(conn, tx, [number], checkin, checkout)).Count > 0)
            throw new HotelRuleException("Room " + number + " is no longer open for those dates.");

        var quote = (await RateBook.LoadAsync(conn, tx, checkin, checkout)).Price(room.Code, checkin, checkout);
        var now = DateTime.UtcNow;
        var paid = input.PaymentStatus == "paid";
        var reminder = checkin.AddDays(-2) > Today() ? checkin.AddDays(-2) : Today();

        // One guest per email. An existing guest row is reused but never rewritten, so a
        // booking can't change the contact details on someone's earlier reservations.
        // The name and phone entered here are kept on the booking itself.
        var guestId = await conn.ExecuteScalarAsync<int>(
            """
            INSERT INTO guest (first_name, last_name, email, phone, created_at, updated_at)
            VALUES (@FirstName, @LastName, @Email, @Phone, @Now, @Now)
            ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id);
            SELECT LAST_INSERT_ID();
            """,
            new { guest.FirstName, guest.LastName, guest.Email, guest.Phone, Now = now }, tx);

        var bookingId = await conn.ExecuteScalarAsync<int>(
            """
            INSERT INTO booking (room_id, room_type_id, guest_id, guest_first_name, guest_last_name, guest_phone,
                                 check_in, check_out, guest_count, status, source, special_requests, total, created_at)
            VALUES (@RoomId, @RoomTypeId, @GuestId, @FirstName, @LastName, @Phone,
                    @Checkin, @Checkout, @Guests, 'confirmed', @Source, @Requests, @Total, @Now);
            SELECT LAST_INSERT_ID();
            """,
            new
            {
                RoomId = room.Id, room.RoomTypeId, GuestId = guestId, guest.FirstName, guest.LastName, guest.Phone,
                Checkin = checkin, Checkout = checkout, Guests = guests, Source = isStaff ? "staff" : "web",
                guest.Requests, quote.Total, Now = now
            }, tx);

        await conn.ExecuteAsync(
            """
            INSERT INTO booking_night (booking_id, night_date, amount, kind, label)
            VALUES (@BookingId, @Date, @Amount, @Kind, @Label)
            """,
            quote.Nights.Select(n => new { BookingId = bookingId, n.Date, n.Amount, n.Kind, n.Label }), tx);

        // Only paid/unpaid and the card-on-file opt-in. Card details never reach the server.
        await conn.ExecuteAsync(
            """
            INSERT INTO payment (booking_id, status, method, card_on_file, amount, paid_at, updated_at)
            VALUES (@BookingId, @Status, @Method, @CardOnFile, @Amount, @PaidAt, @Now)
            """,
            new
            {
                BookingId = bookingId,
                Status = paid ? "paid" : "unpaid",
                Method = isStaff ? null : paid ? "online" : "at_hotel",
                input.CardOnFile,
                Amount = quote.Total,
                PaidAt = paid ? now : (DateTime?)null,
                Now = now
            }, tx);

        await AddMessageAsync(conn, tx, bookingId, "system", "Confirmation emailed to " + guest.Email + ".", now);
        await AddMessageAsync(conn, tx, bookingId, "system",
            "Pre-arrival note (parking, directions, check-in time) scheduled for " + FormatDate(reminder) + ".", now);

        await tx.CommitAsync();
        return (await LoadReservationsAsync(conn, bookingId)).Single();
    }

    public async Task<Reservation> CancelReservationAsync(string id)
    {
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var bookingId = await LockReservationAsync(conn, tx, id);
        var now = DateTime.UtcNow;
        await conn.ExecuteAsync(
            "UPDATE booking SET status = 'cancelled', cancelled_at = COALESCE(cancelled_at, @Now) WHERE id = @Id",
            new { Id = bookingId, Now = now }, tx);
        await AddMessageAsync(conn, tx, bookingId, "system", "Reservation cancelled. Cancellation notice emailed to guest.", now);
        await tx.CommitAsync();
        return (await LoadReservationsAsync(conn, bookingId)).Single();
    }

    public async Task<Reservation> SetPaymentStatusAsync(string id, string? status)
    {
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var bookingId = await LockReservationAsync(conn, tx, id);
        await conn.ExecuteAsync(
            """
            UPDATE payment
            SET status = @Status,
                paid_at = CASE WHEN @Status = 'paid' THEN COALESCE(paid_at, @Now) ELSE NULL END,
                updated_at = @Now
            WHERE booking_id = @Id
            """,
            new { Id = bookingId, Status = status == "paid" ? "paid" : "unpaid", Now = DateTime.UtcNow }, tx);
        await tx.CommitAsync();
        return (await LoadReservationsAsync(conn, bookingId)).Single();
    }

    public async Task<Reservation> SendGuestMessageAsync(string id, string? text)
    {
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var bookingId = await LockReservationAsync(conn, tx, id);
        if (string.IsNullOrWhiteSpace(text)) throw new HotelRuleException("Write a message first.");
        if (text.Trim().Length > 2000) throw new HotelRuleException("Keep messages under 2,000 characters.");
        await AddMessageAsync(conn, tx, bookingId, "staff", text.Trim(), DateTime.UtcNow);
        await tx.CommitAsync();
        return (await LoadReservationsAsync(conn, bookingId)).Single();
    }

    /* ---------- Holds and event blocks ---------- */

    public async Task<List<Hold>> GetHoldsAsync()
    {
        await using var conn = await db.OpenAsync();
        var holds = await conn.QueryAsync<HoldRow>(
            """
            SELECT m.id, r.room_number, m.first_night, m.last_night, m.reason, m.created_at
            FROM maintenance_block m JOIN room r ON r.id = m.room_id
            WHERE r.hotel_id = @HotelId AND m.last_night >= @Today
            ORDER BY m.first_night, m.id
            """,
            new { Db.HotelId, Today = Today() });
        return holds.Select(h => new Hold(HoldPrefix + h.Id, h.RoomNumber,
            DateOnly.FromDateTime(h.FirstNight), DateOnly.FromDateTime(h.LastNight), h.Reason, h.CreatedAt)).ToList();
    }

    public async Task<Hold> CreateHoldAsync(CreateHoldRequest input)
    {
        if (input.Room is not { } number) throw new HotelRuleException("Choose a room.");
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var room = (await LockRoomsAsync(conn, tx, [number])).FirstOrDefault() ?? throw new HotelRuleException("Choose a room.");
        var (start, end) = CheckRange(input.Start, input.End);
        if (string.IsNullOrWhiteSpace(input.Reason)) throw new HotelRuleException("Add a reason so the team knows why.");
        var reason = input.Reason.Trim();
        if (reason.Length > 200) throw new HotelRuleException("Keep the reason under 200 characters.");
        var conflicts = await CommitmentsAsync(conn, tx, [number], start, end.AddDays(1));
        if (conflicts.Count > 0)
            throw new HotelRuleException("Room " + number + " is already taken: " + string.Join("; ", conflicts.Select(Describe)) + ".");

        var now = DateTime.UtcNow;
        var id = await conn.ExecuteScalarAsync<int>(
            """
            INSERT INTO maintenance_block (room_id, first_night, last_night, reason, created_at)
            VALUES (@RoomId, @Start, @End, @Reason, @Now);
            SELECT LAST_INSERT_ID();
            """,
            new { RoomId = room.Id, Start = start, End = end, Reason = reason, Now = now }, tx);
        await tx.CommitAsync();
        return new Hold(HoldPrefix + id, number, start, end, reason, now);
    }

    public async Task ReleaseHoldAsync(string id)
    {
        if (ParseId(HoldPrefix, id) is not { } holdId) return;
        await using var conn = await db.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM maintenance_block WHERE id = @Id", new { Id = holdId });
    }

    public async Task<List<Block>> GetBlocksAsync()
    {
        await using var conn = await db.OpenAsync();
        var blocks = (await conn.QueryAsync<BlockRow>(
            """
            SELECT id, name, first_night, last_night, created_at
            FROM event_block
            WHERE last_night >= @Today
            ORDER BY first_night, id
            """,
            new { Today = Today() })).ToList();
        var rooms = (await conn.QueryAsync<BlockRoomRow>(
            """
            SELECT ebr.event_block_id, r.room_number
            FROM event_block_room ebr JOIN room r ON r.id = ebr.room_id
            WHERE ebr.event_block_id IN @Ids
            ORDER BY r.room_number
            """,
            new { Ids = blocks.Select(b => b.Id).ToArray() })).ToLookup(r => r.EventBlockId, r => r.RoomNumber);
        return blocks.Select(b => new Block(BlockPrefix + b.Id, b.Name,
            DateOnly.FromDateTime(b.FirstNight), DateOnly.FromDateTime(b.LastNight), rooms[b.Id].ToList(), b.CreatedAt)).ToList();
    }

    public async Task<Block> CreateBlockAsync(CreateBlockRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new HotelRuleException("Name the event.");
        var name = input.Name.Trim();
        if (name.Length > 120) throw new HotelRuleException("Keep the event name under 120 characters.");
        var (start, end) = CheckRange(input.Start, input.End);
        if (input.Rooms is not { Count: > 0 }) throw new HotelRuleException("List at least one room.");
        var wanted = input.Rooms.Distinct().ToList();

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var locked = await LockRoomsAsync(conn, tx, wanted);
        var unknown = wanted.Except(locked.Select(r => r.RoomNumber)).ToList();
        if (unknown.Count > 0) throw new HotelRuleException("These aren’t room numbers: " + string.Join(", ", unknown) + ".");
        var conflicts = (await CommitmentsAsync(conn, tx, wanted, start, end.AddDays(1))).ToLookup(c => c.RoomNumber);
        var taken = wanted.Where(n => conflicts[n].Any()).ToList();
        if (taken.Count > 0)
        {
            throw new HotelRuleException("These rooms are already taken on those nights: " +
                string.Join(", ", taken.Select(n => n + " (" + Describe(conflicts[n].First()) + ")")) + ".");
        }

        var now = DateTime.UtcNow;
        var id = await conn.ExecuteScalarAsync<int>(
            """
            INSERT INTO event_block (name, first_night, last_night, created_at)
            VALUES (@Name, @Start, @End, @Now);
            SELECT LAST_INSERT_ID();
            """,
            new { Name = name, Start = start, End = end, Now = now }, tx);
        await conn.ExecuteAsync(
            "INSERT INTO event_block_room (event_block_id, room_id) VALUES (@BlockId, @RoomId)",
            locked.Select(r => new { BlockId = id, RoomId = r.Id }), tx);
        await tx.CommitAsync();
        return new Block(BlockPrefix + id, name, start, end, wanted, now);
    }

    public async Task RemoveBlockAsync(string id)
    {
        if (ParseId(BlockPrefix, id) is not { } blockId) return;
        await using var conn = await db.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM event_block WHERE id = @Id", new { Id = blockId });
    }

    // Sample bookings are priced at the default rates, since Reset clears every special rate.
    public Task ResetDemoDataAsync() =>
        SampleData.ResetAsync(db, DefaultRates, Today(), RateBook.FromDefaults(DefaultRates).Price);

    /* ---------- Rules ---------- */

    // The hotel's calendar day in Tuscaloosa. The server clock is UTC on Azure, which
    // would roll over to tomorrow around 7 PM local time.
    private static readonly TimeZoneInfo HotelTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HotelTimeZone));

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    // Matches fmtDate() in the client: "Fri, Oct 16, 2026".
    private static string FormatDate(DateOnly date) => date.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture);

    private static decimal RoundRate(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static HotelRuleException NotLoaded() =>
        new("The hotel’s rooms aren’t loaded yet. Run API/Database/seed.sql, then try again.");

    // "DC-1042" → 1042. Anything else is treated as not found.
    private static int? ParseId(string prefix, string? id) =>
        id != null && id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string PublicId(string kind, int id) => kind switch
    {
        "reservation" => ReservationPrefix + id,
        "hold" => HoldPrefix + id,
        _ => BlockPrefix + id
    };

    // Same rules and wording as checkStayDates() in the client.
    private static (DateOnly Checkin, DateOnly Checkout) CheckStayDates(string? checkinText, string? checkoutText, bool allowPast, int maxNights)
    {
        var checkin = ParseDate(checkinText) ?? throw new HotelRuleException("Choose an arrival date.");
        var checkout = ParseDate(checkoutText) ?? throw new HotelRuleException("Choose a departure date.");
        if (!allowPast && checkin < Today()) throw new HotelRuleException("Arrival can’t be in the past.");
        if (checkout <= checkin) throw new HotelRuleException("Departure must be after arrival.");
        if (checkout.DayNumber - checkin.DayNumber > maxNights)
            throw new HotelRuleException("Online stays are limited to " + maxNights + " nights. Call us for longer visits.");
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

    // Same rules and wording as the booking and staff forms in the client.
    private static Guest CheckGuest(Guest? input)
    {
        var g = new Guest
        {
            FirstName = (input?.FirstName ?? "").Trim(),
            LastName = (input?.LastName ?? "").Trim(),
            Email = (input?.Email ?? "").Trim(),
            Phone = (input?.Phone ?? "").Trim(),
            Requests = (input?.Requests ?? "").Trim()
        };
        if (g.FirstName.Length == 0) throw new HotelRuleException("Enter a first name.");
        if (g.LastName.Length == 0) throw new HotelRuleException("Enter a last name.");
        if (!EmailPattern.IsMatch(g.Email)) throw new HotelRuleException("Enter an email address like name@example.com.");
        if (g.FirstName.Length > 60 || g.LastName.Length > 60) throw new HotelRuleException("Names can be up to 60 characters.");
        if (g.Email.Length > 254) throw new HotelRuleException("That email address is too long.");
        if (g.Phone.Length > 30) throw new HotelRuleException("That phone number is too long.");
        if (g.Requests.Length > 1000) throw new HotelRuleException("Keep requests under 1,000 characters.");
        return g;
    }

    private static async Task<int> MaxNightsAsync(MySqlConnection conn, MySqlTransaction? tx = null) =>
        await conn.ExecuteScalarAsync<int?>("SELECT max_online_nights FROM hotel WHERE id = @HotelId", new { Db.HotelId }, tx)
            ?? throw NotLoaded();

    private static async Task<Rates> LoadRatesAsync(MySqlConnection conn)
    {
        var types = (await conn.QueryAsync<RoomTypeRow>(
            "SELECT code, weekday_rate, weekend_rate FROM room_type WHERE hotel_id = @HotelId AND code IN ('king', 'suite')",
            new { Db.HotelId })).ToDictionary(t => t.Code);
        if (!types.TryGetValue("king", out var standard) || !types.TryGetValue("suite", out var suite)) throw NotLoaded();
        return new Rates
        {
            Weekday = standard.WeekdayRate,
            Weekend = standard.WeekendRate,
            SuiteWeekday = suite.WeekdayRate,
            SuiteWeekend = suite.WeekendRate
        };
    }

    // Special rates for every room type, one entry per night.
    private static async Task<List<RateOverride>> LoadOverridesAsync(MySqlConnection conn)
    {
        var rates = await conn.QueryAsync<SeasonalRow>(
            """
            SELECT room_type_id, first_night, last_night, nightly_rate, label
            FROM seasonal_rate
            WHERE hotel_id = @HotelId AND room_type_id IS NULL
            """,
            new { Db.HotelId });
        return rates
            .SelectMany(s => Enumerable.Range(0, s.Last.DayNumber - s.First.DayNumber + 1)
                .Select(i => new RateOverride(s.First.AddDays(i), s.NightlyRate, s.Label)))
            .OrderBy(o => o.Date)
            .ToList();
    }

    // Locks the rooms' rows until the transaction ends, so only one writer at a
    // time can check and take a given room. Rows are locked in room-number order,
    // so writers taking several rooms can't deadlock each other.
    private static async Task<List<RoomRow>> LockRoomsAsync(MySqlConnection conn, MySqlTransaction tx, IEnumerable<int> numbers) =>
        (await conn.QueryAsync<RoomRow>(RoomSelect + " AND r.room_number IN @Numbers ORDER BY r.room_number FOR UPDATE OF r",
            new { Db.HotelId, Numbers = numbers.ToArray() }, tx)).ToList();

    private static async Task<int> LockReservationAsync(MySqlConnection conn, MySqlTransaction tx, string id)
    {
        var notFound = new HotelRuleException("Reservation not found.", notFound: true);
        var bookingId = ParseId(ReservationPrefix, id) ?? throw notFound;
        return await conn.ExecuteScalarAsync<int?>("SELECT id FROM booking WHERE id = @Id FOR UPDATE", new { Id = bookingId }, tx)
            ?? throw notFound;
    }

    private static Task AddMessageAsync(MySqlConnection conn, MySqlTransaction tx, int bookingId, string sender, string body, DateTime at) =>
        conn.ExecuteAsync(
            "INSERT INTO booking_message (booking_id, sender, body, created_at) VALUES (@BookingId, @Sender, @Body, @At)",
            new { BookingId = bookingId, Sender = sender, Body = body, At = at }, tx);

    // Everything that occupies a room for any night in [start, endExclusive):
    // confirmed bookings, holds, and event blocks. Null rooms means every room.
    private static async Task<List<CommitmentRow>> CommitmentsAsync(MySqlConnection conn, MySqlTransaction? tx,
        IEnumerable<int>? rooms, DateOnly start, DateOnly endExclusive)
    {
        var roomFilter = rooms == null ? "" : " AND r.room_number IN @Rooms";
        var sql = $"""
            SELECT r.room_number, 'reservation' AS kind, b.id AS ref_id, CONCAT(b.guest_first_name, ' ', b.guest_last_name) AS text
            FROM booking b JOIN room r ON r.id = b.room_id
            WHERE r.hotel_id = @HotelId AND b.status = 'confirmed'
              AND b.check_in < @End AND b.check_out > @Start{roomFilter}
            UNION ALL
            SELECT r.room_number, 'hold', m.id, m.reason
            FROM maintenance_block m JOIN room r ON r.id = m.room_id
            WHERE r.hotel_id = @HotelId
              AND m.first_night < @End AND m.last_night >= @Start{roomFilter}
            UNION ALL
            SELECT r.room_number, 'block', e.id, e.name
            FROM event_block e
            JOIN event_block_room ebr ON ebr.event_block_id = e.id
            JOIN room r ON r.id = ebr.room_id
            WHERE r.hotel_id = @HotelId
              AND e.first_night < @End AND e.last_night >= @Start{roomFilter}
            """;
        return (await conn.QueryAsync<CommitmentRow>(sql,
            new { Db.HotelId, Start = start, End = endExclusive, Rooms = rooms?.ToArray() }, tx)).ToList();
    }

    private static string Describe(CommitmentRow c) => c.Kind switch
    {
        "reservation" => ReservationPrefix + c.RefId + " · " + c.Text,
        "hold" => "Hold · " + c.Text,
        _ => "Event · " + c.Text
    };

    private static async Task<List<Reservation>> LoadReservationsAsync(MySqlConnection conn, int? bookingId)
    {
        var where = bookingId == null ? "" : " WHERE b.id = @Id";
        var param = new { Id = bookingId };
        var bookings = await conn.QueryAsync<BookingRow>(
            $"""
            SELECT b.id, r.room_number, t.code, b.check_in, b.check_out, b.guest_count, b.status, b.source,
                   b.special_requests, b.total, b.created_at,
                   b.guest_first_name AS first_name, b.guest_last_name AS last_name, g.email, b.guest_phone AS phone,
                   p.status AS payment_status, p.card_on_file
            FROM booking b
            JOIN room r ON r.id = b.room_id
            JOIN room_type t ON t.id = b.room_type_id
            JOIN guest g ON g.id = b.guest_id
            LEFT JOIN payment p ON p.booking_id = b.id{where}
            ORDER BY b.id
            """, param);
        var nightWhere = bookingId == null ? "" : " WHERE booking_id = @Id";
        var nights = (await conn.QueryAsync<NightRow>(
            $"SELECT booking_id, night_date, amount, kind, label FROM booking_night{nightWhere} ORDER BY booking_id, night_date",
            param)).ToLookup(n => n.BookingId);
        var messages = (await conn.QueryAsync<MessageRow>(
            $"SELECT booking_id, sender, body, created_at FROM booking_message{nightWhere} ORDER BY created_at, id",
            param)).ToLookup(m => m.BookingId);

        return bookings.Select(b => new Reservation
        {
            Id = ReservationPrefix + b.Id,
            Room = b.RoomNumber,
            Type = b.Code,
            Checkin = DateOnly.FromDateTime(b.CheckIn),
            Checkout = DateOnly.FromDateTime(b.CheckOut),
            Guests = b.GuestCount,
            Guest = new Guest
            {
                FirstName = b.FirstName,
                LastName = b.LastName,
                Email = b.Email,
                Phone = b.Phone,
                Requests = b.SpecialRequests
            },
            Nights = nights[b.Id].Select(n => new NightRate(DateOnly.FromDateTime(n.NightDate), n.Amount, n.Kind, n.Label)).ToList(),
            Total = b.Total,
            PaymentStatus = b.PaymentStatus ?? "unpaid",
            CardOnFile = b.CardOnFile ?? false,
            Source = b.Source,
            Status = b.Status,
            CreatedAt = b.CreatedAt,
            Messages = messages[b.Id].Select(m => new GuestMessage(m.CreatedAt, m.Sender, m.Body)).ToList()
        }).ToList();
    }

    // Nightly prices for a date range: room type rates plus any special rates.
    private sealed class RateBook(Dictionary<string, RoomTypeRow> types, List<SeasonalRow> seasonal)
    {
        public static async Task<RateBook> LoadAsync(MySqlConnection conn, MySqlTransaction? tx, DateOnly from, DateOnly toExclusive)
        {
            var types = (await conn.QueryAsync<RoomTypeRow>(
                "SELECT id, code, weekday_rate, weekend_rate FROM room_type WHERE hotel_id = @HotelId",
                new { Db.HotelId }, tx)).ToDictionary(t => t.Code);
            if (types.Count == 0) throw NotLoaded();
            var seasonal = (await conn.QueryAsync<SeasonalRow>(
                """
                SELECT room_type_id, first_night, last_night, nightly_rate, label
                FROM seasonal_rate
                WHERE hotel_id = @HotelId AND first_night < @To AND last_night >= @From
                """,
                new { Db.HotelId, From = from, To = toExclusive }, tx)).ToList();
            return new RateBook(types, seasonal);
        }

        public static RateBook FromDefaults(Rates rates)
        {
            RoomTypeRow Standard(string code) => new() { Code = code, WeekdayRate = rates.Weekday, WeekendRate = rates.Weekend };
            return new RateBook(new()
            {
                ["king"] = Standard("king"),
                ["queen"] = Standard("queen"),
                ["twin"] = Standard("twin"),
                ["suite"] = new() { Code = "suite", WeekdayRate = rates.SuiteWeekday, WeekendRate = rates.SuiteWeekend }
            }, []);
        }

        // A special rate for this room type wins, then one for every room type, then
        // the weekday or weekend rate. Friday and Saturday nights are weekend nights.
        public NightRate Night(string code, DateOnly date)
        {
            var type = types[code];
            var special = seasonal.Find(s => s.RoomTypeId == type.Id && s.Covers(date))
                ?? seasonal.Find(s => s.RoomTypeId == null && s.Covers(date));
            if (special != null) return new NightRate(date, special.NightlyRate, "special", special.Label);
            var weekend = date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
            return new NightRate(date, weekend ? type.WeekendRate : type.WeekdayRate, weekend ? "weekend" : "weekday");
        }

        public Quote Price(string code, DateOnly checkin, DateOnly checkout)
        {
            var nights = new List<NightRate>();
            for (var d = checkin; d < checkout; d = d.AddDays(1)) nights.Add(Night(code, d));
            return new Quote(nights, nights.Sum(n => n.Amount));
        }
    }

    /* ---------- Row shapes (snake_case columns map to these by name) ---------- */

    private class RoomTypeRow
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Bed { get; set; } = "";
        public string BedLabel { get; set; } = "";
        public int Sleeps { get; set; }
        public int? SizeSqft { get; set; }
        public string Blurb { get; set; } = "";
        public decimal WeekdayRate { get; set; }
        public decimal WeekendRate { get; set; }
    }

    private class RoomRow
    {
        public int Id { get; set; }
        public int RoomNumber { get; set; }
        public int Floor { get; set; }
        public bool IsActive { get; set; }
        public int RoomTypeId { get; set; }
        public string Code { get; set; } = "";
        public int Sleeps { get; set; }
    }

    private class SeasonalRow
    {
        public int? RoomTypeId { get; set; }
        public DateTime FirstNight { get; set; }
        public DateTime LastNight { get; set; }
        public decimal NightlyRate { get; set; }
        public string Label { get; set; } = "";
        public DateOnly First => DateOnly.FromDateTime(FirstNight);
        public DateOnly Last => DateOnly.FromDateTime(LastNight);
        public bool Covers(DateOnly date) => First <= date && date <= Last;
    }

    private class CommitmentRow
    {
        public int RoomNumber { get; set; }
        public string Kind { get; set; } = "";
        public int RefId { get; set; }
        public string Text { get; set; } = "";
    }

    private class ScheduleRow
    {
        public string Kind { get; set; } = "";
        public int RefId { get; set; }
        public DateTime FirstNight { get; set; }
        public DateTime LastNight { get; set; }
        public string Label { get; set; } = "";
    }

    private class BookingRow
    {
        public int Id { get; set; }
        public int RoomNumber { get; set; }
        public string Code { get; set; } = "";
        public DateTime CheckIn { get; set; }
        public DateTime CheckOut { get; set; }
        public int GuestCount { get; set; }
        public string Status { get; set; } = "";
        public string Source { get; set; } = "";
        public string SpecialRequests { get; set; } = "";
        public decimal Total { get; set; }
        public DateTime CreatedAt { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string? PaymentStatus { get; set; }
        public bool? CardOnFile { get; set; }
    }

    private class NightRow
    {
        public int BookingId { get; set; }
        public DateTime NightDate { get; set; }
        public decimal Amount { get; set; }
        public string Kind { get; set; } = "";
        public string? Label { get; set; }
    }

    private class MessageRow
    {
        public int BookingId { get; set; }
        public string Sender { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    private class HoldRow
    {
        public int Id { get; set; }
        public int RoomNumber { get; set; }
        public DateTime FirstNight { get; set; }
        public DateTime LastNight { get; set; }
        public string Reason { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    private class BlockRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public DateTime FirstNight { get; set; }
        public DateTime LastNight { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private class BlockRoomRow
    {
        public int EventBlockId { get; set; }
        public int RoomNumber { get; set; }
    }
}
