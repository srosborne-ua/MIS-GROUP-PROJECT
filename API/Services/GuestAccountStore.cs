using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using API.Database;
using API.Models;
using Dapper;
using MySqlConnector;

namespace API.Services;

// Guest accounts in the guest_account table, as druid_app with parameterized queries.
// Owns every guest sign-in rule; messages are shown to guests as-is.
public class GuestAccountStore(Db db)
{
    public const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 128;

    // Same check as isEmail() in the client.
    private static readonly Regex EmailPattern = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    private const string AccountSelect = """
        SELECT id, first_name, last_name, email, phone, password_hash
        FROM guest_account
        """;

    // Creates the account and returns it with its session stamp, ready to sign in.
    public async Task<(GuestAccount Account, string Stamp)> RegisterAsync(GuestRegisterRequest input)
    {
        var firstName = (input.FirstName ?? "").Trim();
        var lastName = (input.LastName ?? "").Trim();
        var email = (input.Email ?? "").Trim();
        var phone = (input.Phone ?? "").Trim();
        if (firstName.Length == 0) throw new HotelRuleException("Enter a first name.");
        if (lastName.Length == 0) throw new HotelRuleException("Enter a last name.");
        if (firstName.Length > 60 || lastName.Length > 60) throw new HotelRuleException("Names can be up to 60 characters.");
        if (!EmailPattern.IsMatch(email)) throw new HotelRuleException("Enter an email address like name@example.com.");
        if (email.Length > 254) throw new HotelRuleException("That email address is too long.");
        // Same rule as isPhone() in the client.
        if (phone.Count(char.IsAsciiDigit) < 10) throw new HotelRuleException("Enter a phone number with area code.");
        if (phone.Length > 30) throw new HotelRuleException("That phone number is too long.");
        var hash = PasswordHasher.Hash(CheckPassword(input.Password));

        await using var conn = await db.OpenAsync();
        var now = DateTime.UtcNow;
        int id;
        try
        {
            id = await conn.ExecuteScalarAsync<int>(
                """
                INSERT INTO guest_account (first_name, last_name, email, phone, password_hash, last_signed_in_at, created_at, updated_at)
                VALUES (@FirstName, @LastName, @Email, @Phone, @Hash, @Now, @Now, @Now);
                SELECT LAST_INSERT_ID();
                """,
                new { FirstName = firstName, LastName = lastName, Email = email, Phone = phone, Hash = hash, Now = now });
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new HotelRuleException("There’s already an account for " + email + ". Sign in instead.");
        }
        return (new GuestAccount(id, firstName, lastName, email, phone), EmployeeStore.Stamp(hash));
    }

    public async Task<(GuestAccount Account, string Stamp)> SignInAsync(SignInRequest input)
    {
        var email = (input.Email ?? "").Trim();
        var password = input.Password ?? "";
        var mismatch = new HotelRuleException("That email and password don’t match an account.");
        if (email.Length == 0 || password.Length == 0 || password.Length > MaxPasswordLength) throw mismatch;

        await using var conn = await db.OpenAsync();
        var row = await conn.QuerySingleOrDefaultAsync<AccountRow>(AccountSelect + " WHERE email = @Email", new { Email = email });
        // Hash even when there's no account, so timing doesn't reveal which emails have one.
        var matches = PasswordHasher.Verify(password, row?.PasswordHash ?? PasswordHasher.Decoy);
        if (row == null || !matches) throw mismatch;

        await conn.ExecuteAsync("UPDATE guest_account SET last_signed_in_at = @Now WHERE id = @Id", new { Now = DateTime.UtcNow, row.Id });
        return (ToAccount(row), EmployeeStore.Stamp(row.PasswordHash));
    }

    // The signed-in guest, or null once the account's password has changed.
    public async Task<GuestAccount?> GetSessionAsync(int id, string stamp)
    {
        await using var conn = await db.OpenAsync();
        var row = await conn.QuerySingleOrDefaultAsync<AccountRow>(AccountSelect + " WHERE id = @Id", new { Id = id });
        return row != null && EmployeeStore.Stamp(row.PasswordHash) == stamp ? ToAccount(row) : null;
    }

    public const int ResetMinutes = 45;

    private static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // Starts a reset. Returns the guest's first name and a fresh one-time token, or null when
    // there's no such account (or one was just sent). The caller answers the same either way,
    // so the response never reveals which emails have accounts.
    public async Task<(string FirstName, string Token)?> StartResetAsync(string? emailInput)
    {
        var email = (emailInput ?? "").Trim();
        if (!EmailPattern.IsMatch(email) || email.Length > 254) return null;
        await using var conn = await db.OpenAsync();
        var row = await conn.QuerySingleOrDefaultAsync<AccountRow>(AccountSelect + " WHERE email = @Email", new { Email = email });
        if (row == null) return null;
        var now = DateTime.UtcNow;
        // At most one email a minute per account, so nobody can use us to flood an inbox.
        var recent = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM guest_password_reset WHERE guest_account_id = @Id AND created_at > @Since",
            new { row.Id, Since = now.AddMinutes(-1) });
        if (recent > 0) return null;
        await conn.ExecuteAsync("DELETE FROM guest_password_reset WHERE guest_account_id = @Id AND expires_at < @Now", new { row.Id, Now = now });
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        await conn.ExecuteAsync(
            "INSERT INTO guest_password_reset (guest_account_id, token_hash, expires_at, created_at) VALUES (@Id, @Hash, @Expires, @Now)",
            new { row.Id, Hash = HashToken(token), Expires = now.AddMinutes(ResetMinutes), Now = now });
        return (row.FirstName, token);
    }

    // Sets a new password from a reset link. The link works once; every other open link for
    // the account dies with it, and the password change ends any signed-in sessions.
    public async Task ResetPasswordAsync(string? token, string? newPassword)
    {
        var bad = new HotelRuleException("That reset link has expired or was already used. Ask for a new one.");
        var password = CheckPassword(newPassword);
        if (string.IsNullOrEmpty(token) || token.Length > 128) throw bad;
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var accountId = await conn.ExecuteScalarAsync<int?>(
            "SELECT guest_account_id FROM guest_password_reset WHERE token_hash = @Hash AND expires_at > @Now FOR UPDATE",
            new { Hash = HashToken(token), Now = DateTime.UtcNow }, tx) ?? throw bad;
        var changed = await conn.ExecuteAsync(
            "UPDATE guest_account SET password_hash = @Hash, updated_at = @Now WHERE id = @Id",
            new { Hash = PasswordHasher.Hash(password), Now = DateTime.UtcNow, Id = accountId }, tx);
        if (changed == 0) throw bad;
        await conn.ExecuteAsync("DELETE FROM guest_password_reset WHERE guest_account_id = @Id", new { Id = accountId }, tx);
        await tx.CommitAsync();
    }

    private static string CheckPassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new HotelRuleException("Use a password of at least " + MinPasswordLength + " characters.");
        if (password.Length > MaxPasswordLength)
            throw new HotelRuleException("Passwords can be up to " + MaxPasswordLength + " characters.");
        return password;
    }

    private static GuestAccount ToAccount(AccountRow r) => new(r.Id, r.FirstName, r.LastName, r.Email, r.Phone);

    private class AccountRow
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string PasswordHash { get; set; } = "";
    }
}
