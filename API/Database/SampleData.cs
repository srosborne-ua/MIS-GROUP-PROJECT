using API.Models;
using Dapper;
using API.Services;

namespace API.Database;

// The sample reservations, hold, and event block behind "Reset sample data".
// Dates are relative to today. The hotel, room types, and rooms come from
// seed.sql, which an admin runs once by hand.
public static class SampleData
{
    public const int FirstReservationId = 1030;

    // first, last, room, arrive offset, nights, guests, payment, card on file, requests, status
    public static readonly (string First, string Last, int Room, int Offset, int Nights, int Guests, string Payment, bool CardOnFile, string Requests, string Status)[] Reservations =
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

    // room, first-night offset, last-night offset (inclusive), reason
    public static readonly (int Room, int Start, int End, string Reason) Hold = (118, 0, 2, "Maintenance: HVAC service");

    // name, first-night offset, last-night offset (inclusive), rooms
    public static readonly (string Name, int Start, int End, int[] Rooms) Block =
        ("Abernathy–Hollis wedding party", 20, 21, Enumerable.Range(201, 10).ToArray());

    public static string Email(int i) =>
        Reservations[i].First.ToLowerInvariant() + "." + Reservations[i].Last.ToLowerInvariant() + "@example.com";

    public static string Phone(int i) => "(205) 555-01" + (10 + i);

    public static string Source(int i) => i % 3 == 0 ? "staff" : "web";

    public static DateTime CreatedAt(int i, DateTime utcNow) => utcNow.AddDays(-(14 - i));

    private class RoomRow
    {
        public int Id { get; set; }
        public int RoomNumber { get; set; }
        public int RoomTypeId { get; set; }
        public string Code { get; set; } = "";
    }

    // Clears every booking, guest, hold, event block, and special rate, restores the
    // default rates, and writes the sample data, all in one transaction. Rooms and
    // room types are left alone. `price` must quote stays at the default rates.
    public static void Reset(Db db, Rates defaults, DateOnly today, Func<string, DateOnly, DateOnly, Quote> price)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();

        var rooms = conn.Query<RoomRow>(
            """
            SELECT r.id AS Id, r.room_number AS RoomNumber, r.room_type_id AS RoomTypeId, t.code AS Code
            FROM room r JOIN room_type t ON t.id = r.room_type_id
            WHERE r.hotel_id = @HotelId
            """, new { Db.HotelId }, tx).ToDictionary(r => r.RoomNumber);
        if (rooms.Count == 0)
            throw new HotelRuleException("The hotel’s rooms aren’t loaded yet. Run API/Database/seed.sql, then try again.");

        // Children cascade: booking → booking_night, booking_message, payment, service_request;
        // event_block → event_block_room.
        conn.Execute(
            """
            DELETE FROM seasonal_rate WHERE hotel_id = @HotelId;
            DELETE FROM event_block;
            DELETE FROM maintenance_block;
            DELETE FROM booking;
            DELETE FROM guest;
            UPDATE room_type SET weekday_rate = @Weekday, weekend_rate = @Weekend
                WHERE hotel_id = @HotelId AND code <> 'suite';
            UPDATE room_type SET weekday_rate = @SuiteWeekday, weekend_rate = @SuiteWeekend
                WHERE hotel_id = @HotelId AND code = 'suite';
            """,
            new { Db.HotelId, defaults.Weekday, defaults.Weekend, defaults.SuiteWeekday, defaults.SuiteWeekend }, tx);

        var now = DateTime.UtcNow;
        for (var i = 0; i < Reservations.Length; i++)
        {
            var s = Reservations[i];
            var room = rooms[s.Room];
            var checkin = today.AddDays(s.Offset);
            var checkout = checkin.AddDays(s.Nights);
            var quote = price(room.Code, checkin, checkout);
            var created = CreatedAt(i, now);
            var email = Email(i);
            var bookingId = FirstReservationId + i;

            var guestId = conn.ExecuteScalar<int>(
                """
                INSERT INTO guest (first_name, last_name, email, phone, created_at, updated_at)
                VALUES (@First, @Last, @Email, @Phone, @Created, @Created);
                SELECT LAST_INSERT_ID();
                """,
                new { s.First, s.Last, Email = email, Phone = Phone(i), Created = created }, tx);

            conn.Execute(
                """
                INSERT INTO booking (id, room_id, room_type_id, guest_id, check_in, check_out, guest_count,
                                     status, source, special_requests, total, created_at)
                VALUES (@Id, @RoomId, @RoomTypeId, @GuestId, @Checkin, @Checkout, @Guests,
                        @Status, @Source, @Requests, @Total, @Created)
                """,
                new
                {
                    Id = bookingId, RoomId = room.Id, room.RoomTypeId, GuestId = guestId,
                    Checkin = checkin, Checkout = checkout, s.Guests, s.Status,
                    Source = Source(i), s.Requests, quote.Total, Created = created
                }, tx);

            conn.Execute(
                """
                INSERT INTO booking_night (booking_id, night_date, amount, kind, label)
                VALUES (@BookingId, @Date, @Amount, @Kind, @Label)
                """,
                quote.Nights.Select(n => new { BookingId = bookingId, n.Date, n.Amount, n.Kind, n.Label }), tx);

            conn.Execute(
                """
                INSERT INTO payment (booking_id, status, card_on_file, amount, updated_at)
                VALUES (@BookingId, @Status, @CardOnFile, @Amount, @Created)
                """,
                new { BookingId = bookingId, Status = s.Payment, s.CardOnFile, Amount = quote.Total, Created = created }, tx);

            conn.Execute(
                """
                INSERT INTO booking_message (booking_id, sender, body, created_at)
                VALUES (@BookingId, 'system', @Body, @Created)
                """,
                new { BookingId = bookingId, Body = "Confirmation emailed to " + email + ".", Created = created }, tx);
        }

        conn.Execute(
            """
            INSERT INTO maintenance_block (room_id, first_night, last_night, reason, created_at)
            VALUES (@RoomId, @Start, @End, @Reason, @Created)
            """,
            new { RoomId = rooms[Hold.Room].Id, Start = today.AddDays(Hold.Start), End = today.AddDays(Hold.End), Hold.Reason, Created = now }, tx);

        var blockId = conn.ExecuteScalar<int>(
            """
            INSERT INTO event_block (name, first_night, last_night, created_at)
            VALUES (@Name, @Start, @End, @Created);
            SELECT LAST_INSERT_ID();
            """,
            new { Block.Name, Start = today.AddDays(Block.Start), End = today.AddDays(Block.End), Created = now }, tx);

        conn.Execute(
            "INSERT INTO event_block_room (event_block_id, room_id) VALUES (@BlockId, @RoomId)",
            Block.Rooms.Select(n => new { BlockId = blockId, RoomId = rooms[n].Id }), tx);

        tx.Commit();
    }
}
