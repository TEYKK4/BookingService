using RoomBooking.Models;
using RoomBooking.Services;
using Microsoft.Extensions.Options;

namespace RoomBooking.Tests.Integration;

/// <summary>
/// Mints tokens the same way AuthService does. Booking tests do not need the
/// auth service running: RoomBooking verifies the signature on its own.
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

    public static string ForUser(int userId) =>
        Tokens.GenerateToken(new User { Id = userId, Login = $"user{userId}" });
}
