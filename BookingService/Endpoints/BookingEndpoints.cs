using System.Security.Claims;
using BookingService.Contracts;
using BookingService.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingService.Endpoints;

public static class BookingEndpoints
{
    /// <summary>
    /// Log category for these endpoints. ILogger&lt;T&gt; needs a non-static type and
    /// a static class cannot be a type argument, so this stands in for it.
    /// </summary>
    public sealed class Log;

    public static void MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bookings").WithTags("Bookings").RequireAuthorization();

        // Past bookings are history, not a to-do list: they are left out unless asked for.
        group.MapGet("/my", async (
            string? scope,
            BookingDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (user.IdOrNull() is not { } userId)
            {
                return Results.Unauthorized();
            }

            // Parsed by hand because minimal APIs bind enums case-sensitively,
            // and "?scope=past" is what a caller naturally writes.
            if (!Enum.TryParse<BookingScope>(scope ?? nameof(BookingScope.Upcoming), ignoreCase: true, out var wanted))
            {
                return Results.Problem(
                    $"scope must be one of: {string.Join(", ", Enum.GetNames<BookingScope>()).ToLowerInvariant()}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var now = DateTime.UtcNow;
            var mine = db.Bookings.Where(b => b.UserId == userId);

            var selected = wanted switch
            {
                BookingScope.Past => mine.Where(b => b.SlotStart < now).OrderByDescending(b => b.SlotStart),
                BookingScope.All => mine.OrderBy(b => b.SlotStart),
                _ => mine.Where(b => b.SlotStart >= now).OrderBy(b => b.SlotStart),
            };

            var bookings = await selected
                .Select(b => new BookingResponse(
                    b.Id, b.RoomId, b.Room.Name, b.Room.TimeZoneId, b.SlotStart, b.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(bookings);
        });

        group.MapPost("/", async (
            CreateBookingRequest request,
            BookingDbContext db,
            ClaimsPrincipal user,
            ILogger<Log> logger,
            CancellationToken ct) =>
        {
            if (user.IdOrNull() is not { } userId)
            {
                return Results.Unauthorized();
            }

            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == request.RoomId, ct);

            if (room is null)
            {
                return Results.NotFound();
            }

            var zone = BookingHours.ZoneOf(room.TimeZoneId);
            var slotStart = DateTime.SpecifyKind(request.SlotStart.ToUniversalTime(), DateTimeKind.Utc);

            // Validated against the room's local clock, so the rule survives daylight saving.
            if (!BookingHours.IsValidSlot(slotStart, zone))
            {
                return Results.Problem(
                    $"Slots are whole hours between {BookingHours.FirstHour}:00 and " +
                    $"{BookingHours.LastHour}:00 in {room.TimeZoneId}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (slotStart < DateTime.UtcNow)
            {
                return Results.Problem("That slot is in the past.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (slotStart > DateTime.UtcNow.AddDays(BookingHours.DaysBookableAhead))
            {
                return Results.Problem(
                    $"Bookings open {BookingHours.DaysBookableAhead} days ahead.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // Friendly path. It does NOT prevent a double booking on its own -
            // two requests can both pass this check. The unique index does.
            if (await db.Bookings.AnyAsync(b => b.RoomId == room.Id && b.SlotStart == slotStart, ct))
            {
                return Results.Problem("That slot is already booked.", statusCode: StatusCodes.Status409Conflict);
            }

            var booking = new Models.Booking
            {
                RoomId = room.Id,
                UserId = userId,
                SlotStart = slotStart,
                CreatedAt = DateTime.UtcNow,
            };

            db.Bookings.Add(booking);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException
                                              { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Someone booked the same slot between the check above and this insert.
                logger.LogWarning("Race lost on room {RoomId} at {SlotStart}", room.Id, slotStart);

                return Results.Problem("That slot is already booked.", statusCode: StatusCodes.Status409Conflict);
            }

            return Results.Created(
                $"/api/bookings/{booking.Id}",
                new BookingResponse(
                    booking.Id, room.Id, room.Name, room.TimeZoneId, booking.SlotStart, booking.CreatedAt));
        });

        group.MapDelete("/{bookingId:int}", async (
            int bookingId,
            BookingDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (user.IdOrNull() is not { } userId)
            {
                return Results.Unauthorized();
            }

            var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);

            if (booking is null)
            {
                return Results.NotFound();
            }

            // Not 403: telling a stranger the booking exists leaks information.
            if (booking.UserId != userId)
            {
                return Results.NotFound();
            }

            db.Bookings.Remove(booking);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }
}
