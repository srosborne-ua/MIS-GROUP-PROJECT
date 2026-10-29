using API.Models;
using API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Room types, the room list, search, and the single-room check are public (the guest
// site uses them). The room board and schedules are staff only.
[ApiController]
[Route("api/rooms")]
public class RoomsController(HotelStore store) : ControllerBase
{
    // GET api/rooms/types
    [HttpGet("types")]
    [AllowAnonymous]
    public Task<Dictionary<string, RoomType>> GetRoomTypes() => store.GetRoomTypesAsync();

    // GET api/rooms
    [HttpGet]
    [AllowAnonymous]
    public Task<List<Room>> GetRooms() => store.GetRoomsAsync();

    // GET api/rooms/availability?checkin=2026-10-16&checkout=2026-10-18&guests=2&bed=king&floor=any
    // allowPast (stays that have already started) only counts for signed-in staff.
    [HttpGet("availability")]
    [AllowAnonymous]
    public Task<List<AvailabilityGroup>> SearchAvailability([FromQuery] AvailabilityQuery query)
    {
        query.AllowPast = query.AllowPast && StaffSession.IsStaff(User);
        return store.SearchAvailabilityAsync(query);
    }

    // GET api/rooms/212/check?checkin=2026-10-16&checkout=2026-10-18
    [HttpGet("{number:int}/check")]
    [AllowAnonymous]
    public Task<RoomCheck> CheckRoom(int number, [FromQuery] string? checkin, [FromQuery] string? checkout) =>
        store.CheckRoomAsync(number, checkin, checkout);

    // GET api/rooms/board?date=2026-10-16
    [HttpGet("board")]
    public Task<List<RoomBoardEntry>> GetRoomBoard([FromQuery] DateOnly date) => store.GetRoomBoardAsync(date);

    // GET api/rooms/212/schedule
    [HttpGet("{number:int}/schedule")]
    public Task<List<ScheduleItem>> GetRoomSchedule(int number) => store.GetRoomScheduleAsync(number);
}
