using System.Data;
using System.Globalization;
using API.Database;
using API.Models;
using Dapper;

namespace API.Services;

// The staff shift schedule, as druid_app with parameterized queries. Everyone signed in
// can read it; managers add and remove shifts (see ShiftsController).
//
// A shift is a date plus start and end times. When the end is earlier than the start the
// shift runs overnight and ends the next morning. One employee's shifts never overlap:
// adding a shift locks the employee row (SELECT ... FOR UPDATE) and re-checks in the
// same transaction, like bookings do for rooms.
public class ShiftStore(Db db)
{
    private const string ShiftPrefix = "S-";
    private const int MaxListDays = 31;
    private const int MaxShiftHours = 16;
    private const int MaxPastDays = 31;      // late corrections to last month's schedule
    private const int MaxAdvanceDays = 366;  // furthest ahead a shift can be scheduled
    private const int MaxNoteLength = 80;

    private const string ShiftSelect = """
        SELECT s.id, s.employee_id, e.first_name, e.last_name, e.is_active, s.shift_date, s.start_time, s.end_time, s.note
        FROM shift s JOIN employee e ON e.id = s.employee_id
        """;

    // Shifts starting on start .. start + days - 1.
    public async Task<List<Shift>> ListAsync(string? startText, int days)
    {
        var start = ParseDate(startText) ?? throw new HotelRuleException("Choose a week to show.");
        if (days is < 1 or > MaxListDays) throw new HotelRuleException("Show between 1 and " + MaxListDays + " days at a time.");
        CheckHorizon(start);

        await using var conn = await db.OpenAsync();
        var rows = await conn.QueryAsync<ShiftRow>(
            ShiftSelect + " WHERE s.shift_date BETWEEN @Start AND @End ORDER BY s.shift_date, s.start_time, e.last_name, e.first_name",
            new { Start = start, End = start.AddDays(days - 1) });
        return rows.Select(ToShift).ToList();
    }

    public async Task<Shift> CreateAsync(CreateShiftRequest input)
    {
        if (input.EmployeeId is not { } employeeId) throw new HotelRuleException("Choose an employee.");
        var date = ParseDate(input.Date) ?? throw new HotelRuleException("Choose a date.");
        CheckHorizon(date);
        var start = ParseTime(input.Start) ?? throw new HotelRuleException("Enter a start time.");
        var end = ParseTime(input.End) ?? throw new HotelRuleException("Enter an end time.");
        if (start == end) throw new HotelRuleException("A shift can’t start and end at the same time.");
        var (from, to) = Interval(date, start, end);
        if (to - from > TimeSpan.FromHours(MaxShiftHours))
            throw new HotelRuleException("Shifts can be up to " + MaxShiftHours + " hours.");
        var note = (input.Note ?? "").Trim();
        if (note.Length > MaxNoteLength) throw new HotelRuleException("Keep the note under " + MaxNoteLength + " characters.");

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var employee = await conn.QuerySingleOrDefaultAsync<EmployeeLockRow>(
            "SELECT first_name, is_active FROM employee WHERE id = @Id FOR UPDATE", new { Id = employeeId }, tx)
            ?? throw new HotelRuleException("We couldn’t find that employee.", notFound: true);
        if (!employee.IsActive) throw new HotelRuleException(employee.FirstName + "’s account is inactive, so they can’t be scheduled.");

        // An overnight shift from the day before can run into this one, so look a day either side.
        var nearby = await conn.QueryAsync<ShiftRow>(
            ShiftSelect + " WHERE s.employee_id = @EmployeeId AND s.shift_date BETWEEN @From AND @To FOR UPDATE",
            new { EmployeeId = employeeId, From = date.AddDays(-1), To = date.AddDays(1) }, tx);
        var clash = nearby.FirstOrDefault(s =>
        {
            var (otherFrom, otherTo) = Interval(s.ShiftDate, TimeOnly.FromTimeSpan(s.StartTime), TimeOnly.FromTimeSpan(s.EndTime));
            return otherFrom < to && from < otherTo;
        });
        if (clash != null)
            throw new HotelRuleException(employee.FirstName + " already works " + Describe(clash) + ".");

        var id = await conn.ExecuteScalarAsync<int>(
            """
            INSERT INTO shift (employee_id, shift_date, start_time, end_time, note, created_at)
            VALUES (@EmployeeId, @Date, @Start, @End, @Note, @Now);
            SELECT LAST_INSERT_ID();
            """,
            new { EmployeeId = employeeId, Date = date, Start = start.ToTimeSpan(), End = end.ToTimeSpan(), Note = note, Now = DateTime.UtcNow }, tx);
        await tx.CommitAsync();

        var row = await conn.QuerySingleAsync<ShiftRow>(ShiftSelect + " WHERE s.id = @Id", new { Id = id });
        return ToShift(row);
    }

    public async Task RemoveAsync(string id)
    {
        var gone = new HotelRuleException("That shift was already removed.", notFound: true);
        if (ParseId(id) is not { } shiftId) throw gone;
        await using var conn = await db.OpenAsync();
        if (await conn.ExecuteAsync("DELETE FROM shift WHERE id = @Id", new { Id = shiftId }) == 0) throw gone;
    }

    /* ---------- Rules ---------- */

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    // "07:00" or "7:00"; <input type="time"> sends "HH:mm".
    private static TimeOnly? ParseTime(string? value) =>
        TimeOnly.TryParseExact(value, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    private static void CheckHorizon(DateOnly date)
    {
        var today = HotelStore.Today();
        if (date < today.AddDays(-MaxPastDays) || date > today.AddDays(MaxAdvanceDays))
            throw new HotelRuleException("Choose a date between last month and a year from now.");
    }

    // When the shift starts and ends; an end before the start is the next morning.
    private static (DateTime From, DateTime To) Interval(DateOnly date, TimeOnly start, TimeOnly end)
    {
        var from = date.ToDateTime(start);
        var to = (end > start ? date : date.AddDays(1)).ToDateTime(end);
        return (from, to);
    }

    private static int? ParseId(string? id) =>
        id != null && id.StartsWith(ShiftPrefix, StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(id.AsSpan(ShiftPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string Hhmm(TimeSpan t) => t.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" + t.Minutes.ToString("00", CultureInfo.InvariantCulture);

    // "Mon, Oct 26, 07:00–15:00"
    private static string Describe(ShiftRow s) =>
        s.ShiftDate.ToString("ddd, MMM d", CultureInfo.InvariantCulture) + ", " + Hhmm(s.StartTime) + "–" + Hhmm(s.EndTime);

    private static Shift ToShift(ShiftRow s) => new(
        ShiftPrefix + s.Id, s.EmployeeId, s.FirstName + " " + s.LastName, s.IsActive,
        s.ShiftDate, Hhmm(s.StartTime), Hhmm(s.EndTime), s.EndTime < s.StartTime, s.Note);

    private class EmployeeLockRow
    {
        public string FirstName { get; set; } = "";
        public bool IsActive { get; set; }
    }

    private class ShiftRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public bool IsActive { get; set; }
        public DateOnly ShiftDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Note { get; set; } = "";
    }
}
