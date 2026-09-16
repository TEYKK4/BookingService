using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace RoomBooking.Tests.Integration;

/// <summary>
/// Boots the real app in memory against the test database. Nothing is
/// substituted: the same middleware, the same endpoints, the same EF model.
/// Pass admin credentials to exercise the startup seeder; by default none are
/// set, which the app tolerates with a warning.
/// </summary>
public class ApiFactory(string connectionString, string? adminLogin = null, string? adminPassword = null)
    : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["JwtSettings:Key"] = TestJwt.Key,
            ["JwtSettings:Issuer"] = TestJwt.Issuer,
            ["JwtSettings:Audience"] = TestJwt.Audience,
            ["JwtSettings:ExpiryMinutes"] = "60",
        };

        if (adminLogin is not null)
        {
            settings["Admin:Login"] = adminLogin;
            settings["Admin:Password"] = adminPassword;
        }

        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(settings));

        return base.CreateHost(builder);
    }
}
