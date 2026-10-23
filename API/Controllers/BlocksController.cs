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
    public Task<List<Block>> GetBlocks() => store.GetBlocksAsync();

    // POST api/blocks
    [HttpPost]
    public Task<Block> CreateBlock(CreateBlockRequest input) => store.CreateBlockAsync(input);

    // DELETE api/blocks/B-1
    [HttpDelete("{id}")]
    public async Task<IActionResult> RemoveBlock(string id)
    {
        await store.RemoveBlockAsync(id);
        return NoContent();
    }
}
