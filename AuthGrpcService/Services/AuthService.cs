using AuthGrpcService.Data;
using AuthGrpcService.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthGrpcService.Services;

public class AuthService(ILogger<AuthService> logger, AppDbContext db, JwtTokenService jwtTokenService) : Auth.AuthBase
{
    public override async Task<JwtToken> Login(Credentials request, ServerCallContext context)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Login == request.Login);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid login or password"));
        }

        logger.LogInformation("User {Login} logged in", user.Login);

        return new JwtToken { Token = jwtTokenService.GenerateToken(user) };
    }

    public override async Task<JwtToken> Register(Credentials request, ServerCallContext context)
    {
        if (await db.Users.AnyAsync(u => u.Login == request.Login))
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "User with this login already exists"));
        }

        var user = new User
        {
            Login = request.Login,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogWarning("Race condition on register for login {Login}", request.Login);
            throw new RpcException(new Status(StatusCode.AlreadyExists, "User with this login already exists"));
        }

        logger.LogInformation("User {Login} registered", user.Login);

        return new JwtToken { Token = jwtTokenService.GenerateToken(user) };
    }
}
