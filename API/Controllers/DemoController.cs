using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Backs the Staff Portal's "Reset sample data" link.
[ApiController]
[Route("api/demo")]
public class DemoController(HotelStore store, IWebHostEnvironment env, IConfiguration config) : ControllerBase
{
    // POST api/demo/reset
    // Reset deletes every booking, guest, hold, and event block, and there's no staff
    // sign-in yet, so it only runs locally (Development) or while ALLOW_SAMPLE_RESET=true
    // is set on the server. Turn that on, reset once, then turn it off again.
    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        if (!env.IsDevelopment() && !string.Equals(config["ALLOW_SAMPLE_RESET"], "true", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Resetting the sample data is turned off on the live site." });

        await store.ResetDemoDataAsync();
        return NoContent();
    }
}
