using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RoomBooking.Contracts;
using RoomBooking.Data;
using RoomBooking.Models;

namespace RoomBooking.Endpoints;

public static class AdminRoomEndpoints
{
    /// <summary>Log category for these endpoints; a static class cannot be a type argument.</summary>
    public sealed class Log;

    public static void MapAdminRoomEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/rooms")
            .WithTags("Admin: rooms")
            .RequireAuthorization(Policies.Admin);

        // Admins see every room, deactivated ones included - that is the point.
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var rooms = await db.Rooms
                .OrderBy(r => r.Id)
                .Select(r => new AdminRoomResponse(r.Id, r.Name, r.Capacity, r.TimeZoneId, r.IsActive))
                .ToListAsync(ct);

            return Results.Ok(rooms);
        });

        group.MapPost("/", async (
            SaveRoomRequest request,
            IValidator<SaveRoomRequest> validator,
            AppDbContext db,
            ClaimsPrincipal user,
            ILogger<Log> logger,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);

            if (!validation.IsValid)
            {
                return validation.ToProblem();
            }

            var room = new Room
            {
                Name = request.Name.Trim(),
                Capacity = request.Capacity,
                TimeZoneId = request.TimeZoneId,
            };

            db.Rooms.Add(room);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Room {RoomId} ({Name}, {TimeZoneId}) created by admin {UserId}",
                room.Id, room.Name, room.TimeZoneId, user.IdOrNull());

            return Results.Created($"/api/admin/rooms/{room.Id}", ToResponse(room));
        });

        group.MapPut("/{roomId:int}", async (
            int roomId,
            SaveRoomRequest request,
            IValidator<SaveRoomRequest> validator,
            AppDbContext db,
            ClaimsPrincipal user,
            ILogger<Log> logger,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);

            if (!validation.IsValid)
            {
                return validation.ToProblem();
            }

            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);

            if (room is null)
            {
                return Results.NotFound();
            }

            // Bookings are stored as UTC instants that were valid working hours in
            // the old zone. Move the room and they may land at 03:00 local. Refuse
            // rather than silently corrupt what people already booked.
            if (room.TimeZoneId != request.TimeZoneId)
            {
                var now = DateTime.UtcNow;
                var hasUpcoming = await db.Bookings.AnyAsync(b => b.RoomId == roomId && b.SlotStart >= now, ct);

                if (hasUpcoming)
                {
                    return Results.Problem(
                        "Cannot change the time zone of a room that has upcoming bookings.",
                        statusCode: StatusCodes.Status409Conflict);
                }
            }

            room.Name = request.Name.Trim();
            room.Capacity = request.Capacity;
            room.TimeZoneId = request.TimeZoneId;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Room {RoomId} updated by admin {UserId}", room.Id, user.IdOrNull());

            return Results.Ok(ToResponse(room));
        });

        // Deactivation, not deletion: the room disappears for users and takes no new
        // bookings, but its history - and anyone's upcoming booking - stays intact.
        group.MapPost("/{roomId:int}/deactivate", (int roomId, AppDbContext db, ClaimsPrincipal user, ILogger<Log> logger, CancellationToken ct) =>
            SetActive(roomId, false, db, user, logger, ct));

        group.MapPost("/{roomId:int}/activate", (int roomId, AppDbContext db, ClaimsPrincipal user, ILogger<Log> logger, CancellationToken ct) =>
            SetActive(roomId, true, db, user, logger, ct));
    }

    private static async Task<IResult> SetActive(
        int roomId, bool isActive, AppDbContext db, ClaimsPrincipal user, ILogger<Log> logger, CancellationToken ct)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);

        if (room is null)
        {
            return Results.NotFound();
        }

        room.IsActive = isActive;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Room {RoomId} {Action} by admin {UserId}",
            room.Id, isActive ? "activated" : "deactivated", user.IdOrNull());

        return Results.Ok(ToResponse(room));
    }

    private static AdminRoomResponse ToResponse(Room room) =>
        new(room.Id, room.Name, room.Capacity, room.TimeZoneId, room.IsActive);
}
