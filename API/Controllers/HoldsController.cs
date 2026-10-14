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
    public List<Hold> GetHolds() => store.GetHolds();

    // POST api/holds
    [HttpPost]
    public Hold CreateHold(CreateHoldRequest input) => store.CreateHold(input);

    // DELETE api/holds/H-1
    [HttpDelete("{id}")]
    public IActionResult ReleaseHold(string id)
    {
        store.ReleaseHold(id);
        return NoContent();
    }
}
