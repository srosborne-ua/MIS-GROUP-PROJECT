using API.Models;
using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// New rates apply to new searches only; existing reservations keep their totals.
[ApiController]
[Route("api/rates")]
public class RatesController(HotelStore store) : ControllerBase
{
    // GET api/rates
    [HttpGet]
    public Task<RatesResponse> GetRates() => store.GetRatesAsync();

    // PUT api/rates
    [HttpPut]
    public Task<Rates> SaveRates(SaveRatesRequest input) => store.SaveRatesAsync(input);

    // POST api/rates/overrides
    [HttpPost("overrides")]
    public Task<List<RateOverride>> AddRateOverride(RateOverrideRequest input) => store.AddRateOverrideAsync(input);

    // DELETE api/rates/overrides/2026-10-16
    [HttpDelete("overrides/{date}")]
    public Task<List<RateOverride>> RemoveRateOverride(DateOnly date) => store.RemoveRateOverrideAsync(date);

    // GET api/rates/preview?start=2026-10-12&days=14
    [HttpGet("preview")]
    public Task<List<RatePreviewDay>> GetRatePreview([FromQuery] DateOnly start, [FromQuery] int days = 14) =>
        store.GetRatePreviewAsync(start, days);
}
