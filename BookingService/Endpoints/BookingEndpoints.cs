using System.Security.Claims;
using BookingService.Contracts;
using BookingService.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingService.Endpoints;

public static class BookingEndpoints
{
    public static void MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bookings").WithTags("Bookings").RequireAuthorization();

        group.MapGet("/my", async (BookingDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (user.IdOrNull() is not { } userId)
            {
                return Results.Unauthorized();
            }

            var bookings = await db.Bookings
                .Where(b => b.UserId == userId)
                .OrderBy(b => b.SlotStart)
                .Select(b => new BookingResponse(b.Id, b.RoomId, b.Room.Name, b.SlotStart, b.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(bookings);
        });

        group.MapPost("/", async (
            CreateBookingRequest request,
            BookingDbContext db,
            ClaimsPrincipal user,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (user.IdOrNull() is not { } userId)
            {
                return Results.Unauthorized();
            }

            var slotStart = DateTime.SpecifyKind(request.SlotStart.ToUniversalTime(), DateTimeKind.Utc);

            if (!BookingHours.IsValidSlot(slotStart))
            {
                return Results.Problem(
                    $"Slots are whole hours between {BookingHours.FirstHour}:00 and {BookingHours.LastHour}:00 UTC.",
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

            if (!await db.Rooms.AnyAsync(r => r.Id == request.RoomId, ct))
            {
                return Results.NotFound();
            }

            // Friendly path. It does NOT prevent a double booking on its own -
            // two requests can both pass this check. The unique index does.
            if (await db.Bookings.AnyAsync(b => b.RoomId == request.RoomId && b.SlotStart == slotStart, ct))
            {
                return Results.Problem("That slot is already booked.", statusCode: StatusCodes.Status409Conflict);
            }

            var booking = new Models.Booking
            {
                RoomId = request.RoomId,
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
                loggerFactory.CreateLogger("Bookings").LogWarning(
                    "Race lost on room {RoomId} at {SlotStart}", request.RoomId, slotStart);

                return Results.Problem("That slot is already booked.", statusCode: StatusCodes.Status409Conflict);
            }

            var roomName = await db.Rooms.Where(r => r.Id == booking.RoomId).Select(r => r.Name).FirstAsync(ct);

            return Results.Created(
                $"/api/bookings/{booking.Id}",
                new BookingResponse(booking.Id, booking.RoomId, roomName, booking.SlotStart, booking.CreatedAt));
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
