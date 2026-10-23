using API.Models;
using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Holds take a single room out of inventory (maintenance, VIP, etc.).
[ApiController]
[Route("api/holds")]
public class HoldsController(HotelStore store) : ControllerBase
{
    // GET api/holds
    [HttpGet]
    public Task<List<Hold>> GetHolds() => store.GetHoldsAsync();

    // POST api/holds
    [HttpPost]
    public Task<Hold> CreateHold(CreateHoldRequest input) => store.CreateHoldAsync(input);

    // DELETE api/holds/H-1
    [HttpDelete("{id}")]
    public async Task<IActionResult> ReleaseHold(string id)
    {
        await store.ReleaseHoldAsync(id);
        return NoContent();
    }
}
