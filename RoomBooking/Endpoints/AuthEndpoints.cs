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
                return Results.Problem(
                    string.Join(", ", validation.Errors.Select(e => e.ErrorMessage)),
                    statusCode: StatusCodes.Status400BadRequest);
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

            logger.LogInformation("User {Login} registered", user.Login);

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
                return Results.Problem("Invalid login or password", statusCode: StatusCodes.Status401Unauthorized);
            }

            logger.LogInformation("User {Login} logged in", user.Login);

            return Results.Ok(new TokenResponse(tokens.GenerateToken(user)));
        });
    }
}
