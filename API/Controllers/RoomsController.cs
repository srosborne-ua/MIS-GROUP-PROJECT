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
    public Dictionary<string, RoomType> GetRoomTypes() => HotelStore.RoomTypes;

    // GET api/rooms
    [HttpGet]
    public List<Room> GetRooms() => store.GetRooms();

    // GET api/rooms/availability?checkin=2026-10-16&checkout=2026-10-18&guests=2&bed=king&floor=any
    [HttpGet("availability")]
    public List<AvailabilityGroup> SearchAvailability([FromQuery] AvailabilityQuery query) => store.SearchAvailability(query);

    // GET api/rooms/212/check?checkin=2026-10-16&checkout=2026-10-18
    [HttpGet("{number:int}/check")]
    public RoomCheck CheckRoom(int number, [FromQuery] string? checkin, [FromQuery] string? checkout) =>
        store.CheckRoom(number, checkin, checkout);

    // GET api/rooms/board?date=2026-10-16
    [HttpGet("board")]
    public List<RoomBoardEntry> GetRoomBoard([FromQuery] DateOnly date) => store.GetRoomBoard(date);

    // GET api/rooms/212/schedule
    [HttpGet("{number:int}/schedule")]
    public List<ScheduleItem> GetRoomSchedule(int number) => store.GetRoomSchedule(number);
}
