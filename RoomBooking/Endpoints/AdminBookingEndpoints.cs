using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RoomBooking.Contracts;
using RoomBooking.Data;

namespace RoomBooking.Endpoints;

public static class AdminBookingEndpoints
{
    /// <summary>Log category for these endpoints; a static class cannot be a type argument.</summary>
    public sealed class Log;

    public static void MapAdminBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/bookings")
            .WithTags("Admin: bookings")
            .RequireAuthorization(Policies.Admin);

        // Every user's bookings, optionally for one room. Same scope semantics as
        // the user's own list: upcoming by default.
        group.MapGet("/", async (string? scope, int? roomId, AppDbContext db, CancellationToken ct) =>
        {
            if (!BookingScopes.TryParse(scope, out var wanted))
            {
                return BookingScopes.InvalidScopeProblem();
            }

            var now = DateTime.UtcNow;

            // Left join: Bookings.UserId has no foreign key, so a booking must still
            // show up even if its user row is ever gone.
            var query =
                from b in db.Bookings
                join u in db.Users on b.UserId equals u.Id into owners
                from owner in owners.DefaultIfEmpty()
                select new { Booking = b, Login = owner != null ? owner.Login : null };

            if (roomId is { } id)
            {
                query = query.Where(x => x.Booking.RoomId == id);
            }

            query = wanted switch
            {
                BookingScope.Past => query.Where(x => x.Booking.SlotStart < now).OrderByDescending(x => x.Booking.SlotStart),
                BookingScope.All => query.OrderBy(x => x.Booking.SlotStart),
                _ => query.Where(x => x.Booking.SlotStart >= now).OrderBy(x => x.Booking.SlotStart),
            };

            var bookings = await query
                .Select(x => new AdminBookingResponse(
                    x.Booking.Id, x.Booking.RoomId, x.Booking.Room.Name, x.Booking.Room.TimeZoneId,
                    x.Booking.SlotStart, x.Booking.UserId, x.Login ?? "(unknown user)", x.Booking.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(bookings);
        });

        // An admin may cancel anyone's booking. Unlike the user endpoint this is a
        // real 404 when missing - there is nothing to hide from an admin.
        group.MapDelete("/{bookingId:int}", async (
            int bookingId,
            AppDbContext db,
            ClaimsPrincipal user,
            ILogger<Log> logger,
            CancellationToken ct) =>
        {
            var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);

            if (booking is null)
            {
                return Results.NotFound();
            }

            db.Bookings.Remove(booking);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Booking {BookingId} of user {OwnerId} cancelled by admin {UserId}",
                bookingId, booking.UserId, user.IdOrNull());

            return Results.NoContent();
        });
    }
}
