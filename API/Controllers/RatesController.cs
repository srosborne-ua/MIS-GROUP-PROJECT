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
    public RatesResponse GetRates() => store.GetRates();

    // PUT api/rates
    [HttpPut]
    public Rates SaveRates(SaveRatesRequest input) => store.SaveRates(input);

    // POST api/rates/overrides
    [HttpPost("overrides")]
    public List<RateOverride> AddRateOverride(RateOverrideRequest input) => store.AddRateOverride(input);

    // DELETE api/rates/overrides/2026-10-16
    [HttpDelete("overrides/{date}")]
    public List<RateOverride> RemoveRateOverride(DateOnly date) => store.RemoveRateOverride(date);

    // GET api/rates/preview?start=2026-10-12&days=14
    [HttpGet("preview")]
    public List<RatePreviewDay> GetRatePreview([FromQuery] DateOnly start, [FromQuery] int days = 14) =>
        store.GetRatePreview(start, days);
}
