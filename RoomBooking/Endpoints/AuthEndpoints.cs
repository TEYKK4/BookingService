using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomBooking.Contracts;
using RoomBooking.Data;
using RoomBooking.Models;
using RoomBooking.Services;

namespace RoomBooking.Endpoints;

public static class AuthEndpoints
{
    /// <summary>Log category for these endpoints; a static class cannot be a type argument.</summary>
    public sealed class Log;

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (
            CredentialsRequest request,
            IValidator<CredentialsRequest> validator,
            AppDbContext db,
            JwtTokenService tokens,
            ILogger<Log> logger,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);

            if (!validation.IsValid)
            {
                return validation.ToProblem();
            }

            // Friendly path. It does NOT prevent two accounts with one login on its
            // own - two requests can both pass this check. The unique index does.
            if (await db.Users.AnyAsync(u => u.Login == request.Login, ct))
            {
                return Results.Problem("User with this login already exists", statusCode: StatusCodes.Status409Conflict);
            }

            var user = new User
            {
                Login = request.Login,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            };

            db.Users.Add(user);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException
                                              { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Someone registered the same login between the check above and this insert.
                logger.LogWarning("Race lost on register for login {Login}", request.Login);
                return Results.Problem("User with this login already exists", statusCode: StatusCodes.Status409Conflict);
            }

            logger.LogInformation("User {UserId} ({Login}) registered", user.Id, user.Login);

            return Results.Ok(new TokenResponse(tokens.GenerateToken(user)));
        });

        // Not validated on purpose: a malformed login matches nobody, and the
        // answer must not reveal whether it was the login or the password.
        group.MapPost("/login", async (
            CredentialsRequest request,
            AppDbContext db,
            JwtTokenService tokens,
            ILogger<Log> logger,
            CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Login == request.Login, ct);

            if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password ?? string.Empty, user.PasswordHash))
            {
                // Logged so that a burst of these is visible - it is how brute force
                // shows up. The attempted login goes in; the password never does.
                logger.LogWarning("Failed login for {Login}", request.Login);
                return Results.Problem("Invalid login or password", statusCode: StatusCodes.Status401Unauthorized);
            }

            logger.LogInformation("User {UserId} ({Login}) logged in", user.Id, user.Login);

            return Results.Ok(new TokenResponse(tokens.GenerateToken(user)));
        });

        // Who am I - read straight from the token, no database. Lets the client
        // decide what to show (the admin area) without decoding the JWT itself.
        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            if (user.IdOrNull() is not { } userId)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new MeResponse(
                userId,
                user.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
                user.FindFirstValue(ClaimTypes.Role) ?? nameof(UserRole.User)));
        }).RequireAuthorization();
    }
}
