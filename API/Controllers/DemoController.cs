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
    public IActionResult Reset()
    {
        store.ResetDemoData();
        return NoContent();
    }
}
