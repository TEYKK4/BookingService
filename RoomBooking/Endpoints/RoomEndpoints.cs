using System.Security.Claims;
using RoomBooking.Contracts;
using RoomBooking.Data;
using Microsoft.EntityFrameworkCore;

namespace RoomBooking.Endpoints;

public static class RoomEndpoints
{
    public static void MapRoomEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rooms").WithTags("Rooms");

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var rooms = await db.Rooms
                .Where(r => r.IsActive)
                .OrderBy(r => r.Capacity)
                .Select(r => new RoomResponse(r.Id, r.Name, r.Capacity, r.TimeZoneId))
                .ToListAsync(ct);

            return Results.Ok(rooms);
        });

        // `date` is a calendar day in the room's own time zone, not in UTC.
        group.MapGet("/{roomId:int}/availability", async (
            int roomId,
            DateOnly? date,
            AppDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);

            // A deactivated room looks like it does not exist - same as in the list.
            if (room is null || !room.IsActive)
            {
                return Results.NotFound();
            }

            var zone = BookingHours.ZoneOf(room.TimeZoneId);
            var day = date ?? BookingHours.TodayIn(zone);

            var slots = BookingHours.SlotsOn(day, zone).ToList();

            if (slots.Count == 0)
            {
                return Results.Ok(new List<SlotResponse>());
            }

            // One range query instead of a lookup per slot.
            var first = slots[0];
            var last = slots[^1];

            var taken = await db.Bookings
                .Where(b => b.RoomId == roomId && b.SlotStart >= first && b.SlotStart <= last)
                .Select(b => new { b.SlotStart, b.UserId })
                .ToListAsync(ct);

            var currentUserId = user.IdOrNull();

            var response = slots
                .Select(slot =>
                {
                    var booking = taken.FirstOrDefault(t => t.SlotStart == slot);
                    return new SlotResponse(
                        slot,
                        IsTaken: booking is not null,
                        IsMine: booking is not null && booking.UserId == currentUserId);
                })
                .ToList();

            return Results.Ok(response);
        });
    }
}
