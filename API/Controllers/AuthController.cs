using API.Models;
using API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

// Staff Portal sign-in. The session is an HttpOnly cookie (see StaffSession).
[ApiController]
[Route("api/auth")]
public class AuthController(EmployeeStore employees) : ControllerBase
{
    // POST api/auth/sign-in
    [HttpPost("sign-in")]
    [AllowAnonymous]
    [EnableRateLimiting("sign-in")]
    public async Task<Employee> SignIn(SignInRequest input)
    {
        var (employee, stamp) = await employees.SignInAsync(input);
        await StaffSession.SignInAsync(HttpContext, employee, stamp);
        return employee;
    }

    // POST api/auth/sign-out
    [HttpPost("sign-out")]
    [AllowAnonymous]
    public async Task<IActionResult> SignOutStaff()
    {
        await StaffSession.SignOutAsync(HttpContext);
        return NoContent();
    }

    // GET api/auth/me: the signed-in employee, or 401 when nobody is signed in.
    [HttpGet("me")]
    public Employee Me() => StaffSession.Current(User);

    // PUT api/auth/password
    [HttpPut("password")]
    [EnableRateLimiting("sign-in")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest input)
    {
        var stamp = await employees.ChangeOwnPasswordAsync(StaffSession.EmployeeId(User), input);
        // Re-issue this session's cookie with the new stamp; other sessions end on their next check.
        await StaffSession.SignInAsync(HttpContext, StaffSession.Current(User), stamp);
        return NoContent();
    }
}
