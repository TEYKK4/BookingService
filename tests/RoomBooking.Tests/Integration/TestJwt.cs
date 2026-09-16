using Microsoft.Extensions.Options;
using RoomBooking.Models;
using RoomBooking.Services;

namespace RoomBooking.Tests.Integration;

/// <summary>
/// Mints tokens the same way the app does, with the same key the test host is
/// configured with - so the app accepts them without any user existing.
/// </summary>
public static class TestJwt
{
    public const string Issuer = "TestIssuer";
    public const string Audience = "TestAudience";
    public const string Key = "test-signing-key-that-is-long-enough-for-hmac-sha256";

    private static readonly JwtTokenService Tokens = new(Options.Create(new JwtSettings
    {
        Issuer = Issuer,
        Audience = Audience,
        Key = Key,
        ExpiryMinutes = 60,
    }));

    public static string ForUser(int userId, UserRole role = UserRole.User) =>
        Tokens.GenerateToken(new User { Id = userId, Login = $"user{userId}", Role = role });
}
