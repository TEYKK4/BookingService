using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using RoomBooking.Models;
using RoomBooking.Services;
using Microsoft.Extensions.Options;
using Shouldly;

namespace RoomBooking.Tests.Unit;

public class JwtTokenServiceTests
{
    private static readonly JwtSettings Settings = new()
    {
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        Key = "test-signing-key-that-is-long-enough-for-hmac-sha256",
        ExpiryMinutes = 60,
    };

    private readonly JwtTokenService _service = new(Options.Create(Settings));

    private static JwtSecurityToken Decode(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    [Fact]
    public void Token_carries_the_user_id_and_login()
    {
        var user = new User { Id = 42, Login = "bob" };

        var decoded = Decode(_service.GenerateToken(user));

        decoded.Claims.ShouldContain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == "42");
        decoded.Claims.ShouldContain(c => c.Type == ClaimTypes.Name && c.Value == "bob");
    }

    [Fact]
    public void Token_uses_the_configured_issuer_and_audience()
    {
        var decoded = Decode(_service.GenerateToken(new User { Id = 1, Login = "bob" }));

        decoded.Issuer.ShouldBe(Settings.Issuer);
        decoded.Audiences.ShouldContain(Settings.Audience);
    }

    [Fact]
    public void Token_expires_after_the_configured_number_of_minutes()
    {
        var decoded = Decode(_service.GenerateToken(new User { Id = 1, Login = "bob" }));

        decoded.ValidTo.ShouldBe(DateTime.UtcNow.AddMinutes(Settings.ExpiryMinutes), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Never_puts_the_password_hash_in_the_token()
    {
        var user = new User { Id = 1, Login = "bob", PasswordHash = "$2a$11$somethingsecret" };

        var token = _service.GenerateToken(user);

        token.ShouldNotContain("somethingsecret");
        Decode(token).Claims.ShouldNotContain(c => c.Value.Contains("$2a$"));
    }
}
