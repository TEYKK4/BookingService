using System.Security.Claims;
using BookingService.Contracts;
using BookingService.Data;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Endpoints;

public static class RoomEndpoints
{
    public static void MapRoomEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rooms").WithTags("Rooms");

        group.MapGet("/", async (BookingDbContext db, CancellationToken ct) =>
        {
            var rooms = await db.Rooms
                .OrderBy(r => r.Capacity)
                .Select(r => new RoomResponse(r.Id, r.Name, r.Capacity))
                .ToListAsync(ct);

            return Results.Ok(rooms);
        });

        group.MapGet("/{roomId:int}/availability", async (
            int roomId,
            DateOnly? date,
            BookingDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

            if (!await db.Rooms.AnyAsync(r => r.Id == roomId, ct))
            {
                return Results.NotFound();
            }

            var dayStart = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var dayEnd = dayStart.AddDays(1);

            var taken = await db.Bookings
                .Where(b => b.RoomId == roomId && b.SlotStart >= dayStart && b.SlotStart < dayEnd)
                .Select(b => new { b.SlotStart, b.UserId })
                .ToListAsync(ct);

            var currentUserId = user.IdOrNull();

            var slots = BookingHours.SlotsOn(day)
                .Select(slot =>
                {
                    var booking = taken.FirstOrDefault(t => t.SlotStart == slot);
                    return new SlotResponse(
                        slot,
                        IsTaken: booking is not null,
                        IsMine: booking is not null && booking.UserId == currentUserId);
                })
                .ToList();

            return Results.Ok(slots);
        });
    }
}
