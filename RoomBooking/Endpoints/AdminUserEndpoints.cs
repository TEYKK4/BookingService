using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RoomBooking.Contracts;
using RoomBooking.Data;
using RoomBooking.Models;

namespace RoomBooking.Endpoints;

public static class AdminUserEndpoints
{
    /// <summary>Log category for these endpoints; a static class cannot be a type argument.</summary>
    public sealed class Log;

    public static void MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
            .WithTags("Admin: users")
            .RequireAuthorization(Policies.Admin);

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            // Role is projected as the enum and turned into text in memory: the
            // conversion is a mapping detail, not something to push into SQL.
            var users = await db.Users
                .OrderBy(u => u.Id)
                .Select(u => new { u.Id, u.Login, u.Role })
                .ToListAsync(ct);

            return Results.Ok(users.Select(u => new AdminUserResponse(u.Id, u.Login, u.Role.ToString())));
        });

        group.MapPut("/{userId:int}/role", async (
            int userId,
            SetRoleRequest request,
            AppDbContext db,
            ClaimsPrincipal actor,
            ILogger<Log> logger,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
            {
                return Results.Problem(
                    $"role must be one of: {string.Join(", ", Enum.GetNames<UserRole>())}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // Taking your own admin role away is the one change that cannot be undone
            // from the UI. Refuse it; another admin has to do it.
            if (actor.IdOrNull() == userId && role != UserRole.Admin)
            {
                return Results.Problem(
                    "You cannot remove your own admin role.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

            if (user is null)
            {
                return Results.NotFound();
            }

            user.Role = role;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "User {UserId} ({Login}) set to {Role} by admin {ActorId}",
                user.Id, user.Login, role, actor.IdOrNull());

            return Results.Ok(new AdminUserResponse(user.Id, user.Login, user.Role.ToString()));
        });
    }
}
