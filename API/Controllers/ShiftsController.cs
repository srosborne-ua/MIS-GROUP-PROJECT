using API.Models;
using API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// The staff shift schedule. Any signed-in employee can see it; managers change it.
[ApiController]
[Route("api/shifts")]
public class ShiftsController(ShiftStore shifts) : ControllerBase
{
    // GET api/shifts?start=2026-10-26&days=7
    [HttpGet]
    public Task<List<Shift>> GetShifts([FromQuery] string? start, [FromQuery] int days = 7) => shifts.ListAsync(start, days);

    // POST api/shifts
    [HttpPost]
    [Authorize(Roles = EmployeeStore.Manager)]
    public Task<Shift> CreateShift(CreateShiftRequest input) => shifts.CreateAsync(input);

    // DELETE api/shifts/S-4
    [HttpDelete("{id}")]
    [Authorize(Roles = EmployeeStore.Manager)]
    public async Task<IActionResult> RemoveShift(string id)
    {
        await shifts.RemoveAsync(id);
        return NoContent();
    }
}
