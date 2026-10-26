namespace API.Models;

// Staff Portal accounts. The password hash never leaves the server.
// Role: "manager" or "staff" (see EmployeeStore.Manager).
public record Employee(int Id, string FirstName, string LastName, string Email, string Role, bool Active,
    DateTime? LastSignedInAt, DateTime? CreatedAt);

public class SignInRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}

public class CreateEmployeeRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
}

public class ChangePasswordRequest
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
}

public class SetPasswordRequest
{
    public string? Password { get; set; }
}

public class EmployeeActiveRequest
{
    public bool Active { get; set; }
}

public class EmployeeRoleRequest
{
    public string? Role { get; set; }
}

/* ---------- Shift schedule ---------- */

// Start and End are "HH:mm" in hotel-local time. Overnight shifts end the morning after Date.
public record Shift(string Id, int EmployeeId, string EmployeeName, bool EmployeeActive,
    DateOnly Date, string Start, string End, bool Overnight, string Note);

public class CreateShiftRequest
{
    public int? EmployeeId { get; set; }
    public string? Date { get; set; }
    public string? Start { get; set; }
    public string? End { get; set; }
    public string? Note { get; set; }
}
