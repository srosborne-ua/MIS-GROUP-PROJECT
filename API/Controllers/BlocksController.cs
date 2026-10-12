using API.Models;
using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Event blocks take a set of rooms out of inventory for a date range.
[ApiController]
[Route("api/blocks")]
public class BlocksController(HotelStore store) : ControllerBase
{
    // GET api/blocks
    [HttpGet]
    public List<Block> GetBlocks() => store.GetBlocks();

    // POST api/blocks
    [HttpPost]
    public Block CreateBlock(CreateBlockRequest input) => store.CreateBlock(input);

    // DELETE api/blocks/B-1
    [HttpDelete("{id}")]
    public IActionResult RemoveBlock(string id)
    {
        store.RemoveBlock(id);
        return NoContent();
    }
}
