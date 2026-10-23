using API.Models;
using API.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/rooms")]
public class RoomsController(HotelStore store) : ControllerBase
{
    // GET api/rooms/types
    [HttpGet("types")]
    public Task<Dictionary<string, RoomType>> GetRoomTypes() => store.GetRoomTypesAsync();

    // GET api/rooms
    [HttpGet]
    public Task<List<Room>> GetRooms() => store.GetRoomsAsync();

    // GET api/rooms/availability?checkin=2026-10-16&checkout=2026-10-18&guests=2&bed=king&floor=any
    [HttpGet("availability")]
    public Task<List<AvailabilityGroup>> SearchAvailability([FromQuery] AvailabilityQuery query) => store.SearchAvailabilityAsync(query);

    // GET api/rooms/212/check?checkin=2026-10-16&checkout=2026-10-18
    [HttpGet("{number:int}/check")]
    public Task<RoomCheck> CheckRoom(int number, [FromQuery] string? checkin, [FromQuery] string? checkout) =>
        store.CheckRoomAsync(number, checkin, checkout);

    // GET api/rooms/board?date=2026-10-16
    [HttpGet("board")]
    public Task<List<RoomBoardEntry>> GetRoomBoard([FromQuery] DateOnly date) => store.GetRoomBoardAsync(date);

    // GET api/rooms/212/schedule
    [HttpGet("{number:int}/schedule")]
    public Task<List<ScheduleItem>> GetRoomSchedule(int number) => store.GetRoomScheduleAsync(number);
}
