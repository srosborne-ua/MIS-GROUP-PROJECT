using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using API.Database;
using API.Models;
using Dapper;
using MySqlConnector;

namespace API.Services;

// Staff accounts in the employee table, as druid_app with parameterized queries.
// Owns every sign-in and account rule; messages are shown to staff as-is.
public class EmployeeStore(Db db)
{
    public const int MinPasswordLength = 12;
    private const int MaxPasswordLength = 128;

    // Roles. Managers also manage staff accounts, the shift schedule, and Reset sample data
    // ([Authorize(Roles = EmployeeStore.Manager)]); everyone else is staff.
    public const string Manager = "manager";
    public const string Staff = "staff";

    // Same check as isEmail() in the client.
    private static readonly Regex EmailPattern = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    private const string EmployeeSelect = """
        SELECT id, first_name, last_name, email, password_hash, role, is_active, last_signed_in_at, created_at
        FROM employee
        """;

    // Returns the employee when the email and password match an active account.
    public async Task<(Employee Employee, string Stamp)> SignInAsync(SignInRequest input)
    {
        var email = (input.Email ?? "").Trim();
        var password = input.Password ?? "";
        var mismatch = new HotelRuleException("That email and password don’t match an active staff account.");
        if (email.Length == 0 || password.Length == 0 || password.Length > MaxPasswordLength) throw mismatch;

        await using var conn = await db.OpenAsync();
        var row = await conn.QuerySingleOrDefaultAsync<EmployeeRow>(EmployeeSelect + " WHERE email = @Email", new { Email = email });
        // Hash even when there's no account, so timing doesn't reveal which emails are staff.
        var matches = PasswordHasher.Verify(password, row?.PasswordHash ?? PasswordHasher.Decoy);
        if (row == null || !matches || !row.IsActive) throw mismatch;

        var now = DateTime.UtcNow;
        await conn.ExecuteAsync("UPDATE employee SET last_signed_in_at = @Now WHERE id = @Id", new { Now = now, row.Id });
        row.LastSignedInAt = now;
        return (ToEmployee(row), Stamp(row.PasswordHash));
    }

    // The signed-in employee, or null once they've been deactivated or their password changed.
    public async Task<Employee?> GetSessionAsync(int id, string stamp)
    {
        await using var conn = await db.OpenAsync();
        var row = await conn.QuerySingleOrDefaultAsync<EmployeeRow>(EmployeeSelect + " WHERE id = @Id", new { Id = id });
        return row is { IsActive: true } && Stamp(row.PasswordHash) == stamp ? ToEmployee(row) : null;
    }

    public async Task<List<Employee>> ListAsync()
    {
        await using var conn = await db.OpenAsync();
        var rows = await conn.QueryAsync<EmployeeRow>(EmployeeSelect + " ORDER BY is_active DESC, last_name, first_name");
        return rows.Select(ToEmployee).ToList();
    }

    public async Task<Employee> CreateAsync(CreateEmployeeRequest input)
    {
        var firstName = (input.FirstName ?? "").Trim();
        var lastName = (input.LastName ?? "").Trim();
        var email = (input.Email ?? "").Trim();
        if (firstName.Length == 0) throw new HotelRuleException("Enter a first name.");
        if (lastName.Length == 0) throw new HotelRuleException("Enter a last name.");
        if (firstName.Length > 60 || lastName.Length > 60) throw new HotelRuleException("Names can be up to 60 characters.");
        if (!EmailPattern.IsMatch(email)) throw new HotelRuleException("Enter an email address like name@example.com.");
        if (email.Length > 254) throw new HotelRuleException("That email address is too long.");
        var role = CheckRole(input.Role ?? Staff);
        var hash = PasswordHasher.Hash(CheckPassword(input.Password));

        await using var conn = await db.OpenAsync();
        var now = DateTime.UtcNow;
        int id;
        try
        {
            id = await conn.ExecuteScalarAsync<int>(
                """
                INSERT INTO employee (first_name, last_name, email, password_hash, role, is_active, created_at, updated_at)
                VALUES (@FirstName, @LastName, @Email, @Hash, @Role, TRUE, @Now, @Now);
                SELECT LAST_INSERT_ID();
                """,
                new { FirstName = firstName, LastName = lastName, Email = email, Hash = hash, Role = role, Now = now });
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new HotelRuleException("There’s already a staff account for " + email + ".");
        }
        return (await LoadAsync(conn, id))!;
    }

    // Deactivated employees can't sign in, and any session they have ends on its next check.
    public async Task<Employee> SetActiveAsync(int id, bool active, int signedInId)
    {
        if (id == signedInId && !active) throw new HotelRuleException("You can’t deactivate your own account.");
        await using var conn = await db.OpenAsync();
        var current = await LoadAsync(conn, id) ?? throw NotFound();
        if (current.Active == active)
            throw new HotelRuleException(current.FirstName + " " + current.LastName + " is already " + (active ? "active" : "inactive") + ".");
        if (await conn.ExecuteAsync(
                "UPDATE employee SET is_active = @Active, updated_at = @Now WHERE id = @Id AND is_active <> @Active",
                new { Active = active, Now = DateTime.UtcNow, Id = id }) == 0)
            throw NotFound();
        return (await LoadAsync(conn, id))!;
    }

    // Managers can't change their own role, so there's always at least one active manager
    // (the one making the change). Takes effect on the employee's next session check.
    public async Task<Employee> SetRoleAsync(int id, string? role, int signedInId)
    {
        var newRole = CheckRole(role);
        if (id == signedInId) throw new HotelRuleException("You can’t change your own role. Ask another manager.");
        await using var conn = await db.OpenAsync();
        var current = await LoadAsync(conn, id) ?? throw NotFound();
        if (current.Role == newRole)
            throw new HotelRuleException(current.FirstName + " " + current.LastName + " is already " + (newRole == Manager ? "a manager" : "staff") + ".");
        if (await conn.ExecuteAsync(
                "UPDATE employee SET role = @Role, updated_at = @Now WHERE id = @Id AND role <> @Role",
                new { Role = newRole, Now = DateTime.UtcNow, Id = id }) == 0)
            throw NotFound();
        return (await LoadAsync(conn, id))!;
    }

    // Sets another employee's password, for example when they've forgotten it.
    // Their existing sessions end on the next check.
    public async Task SetPasswordAsync(int id, string? password, int signedInId)
    {
        if (id == signedInId) throw new HotelRuleException("Use “Change my password” for your own account.");
        await UpdateHashAsync(id, PasswordHasher.Hash(CheckPassword(password)));
    }

    // Returns the new session stamp so the caller can keep its own session signed in.
    public async Task<string> ChangeOwnPasswordAsync(int id, ChangePasswordRequest input)
    {
        await using (var conn = await db.OpenAsync())
        {
            var hash = await conn.ExecuteScalarAsync<string?>("SELECT password_hash FROM employee WHERE id = @Id", new { Id = id })
                ?? throw NotFound();
            if (!PasswordHasher.Verify(input.CurrentPassword ?? "", hash))
                throw new HotelRuleException("Your current password isn’t right.");
        }
        var newPassword = CheckPassword(input.NewPassword);
        if (newPassword == input.CurrentPassword) throw new HotelRuleException("Choose a password different from your current one.");
        var newHash = PasswordHasher.Hash(newPassword);
        await UpdateHashAsync(id, newHash);
        return Stamp(newHash);
    }

    /* ---------- Rules ---------- */

    private static string CheckRole(string? role) =>
        role is Manager or Staff ? role : throw new HotelRuleException("Choose manager or staff.");

    private static string CheckPassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new HotelRuleException("Use a password of at least " + MinPasswordLength + " characters.");
        if (password.Length > MaxPasswordLength)
            throw new HotelRuleException("Passwords can be up to " + MaxPasswordLength + " characters.");
        return password;
    }

    // A short fingerprint of the password hash, kept in the session cookie. Changing the
    // password changes it, which ends every other session for that employee.
    public static string Stamp(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)))[..16];

    private static HotelRuleException NotFound() => new("We couldn’t find that staff account.", notFound: true);

    private async Task UpdateHashAsync(int id, string hash)
    {
        await using var conn = await db.OpenAsync();
        if (await conn.ExecuteAsync("UPDATE employee SET password_hash = @Hash, updated_at = @Now WHERE id = @Id",
                new { Hash = hash, Now = DateTime.UtcNow, Id = id }) == 0)
            throw NotFound();
    }

    private static async Task<Employee?> LoadAsync(MySqlConnection conn, int id)
    {
        var row = await conn.QuerySingleOrDefaultAsync<EmployeeRow>(EmployeeSelect + " WHERE id = @Id", new { Id = id });
        return row == null ? null : ToEmployee(row);
    }

    private static Employee ToEmployee(EmployeeRow r) =>
        new(r.Id, r.FirstName, r.LastName, r.Email, r.Role, r.IsActive, r.LastSignedInAt, r.CreatedAt);

    private class EmployeeRow
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Role { get; set; } = Staff;
        public bool IsActive { get; set; }
        public DateTime? LastSignedInAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
