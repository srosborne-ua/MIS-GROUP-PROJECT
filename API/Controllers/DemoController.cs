using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Backs the Staff Portal's "Reset sample data" link.
[ApiController]
[Route("api/demo")]
public class DemoController(HotelStore store) : ControllerBase
{
    // POST api/demo/reset
    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        await store.ResetDemoDataAsync();
        return NoContent();
    }
}
