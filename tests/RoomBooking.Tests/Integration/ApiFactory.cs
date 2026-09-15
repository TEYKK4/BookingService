using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace RoomBooking.Tests.Integration;

/// <summary>
/// Boots the real app in memory against the test database. Nothing is
/// substituted: the same middleware, the same endpoints, the same EF model.
/// </summary>
public class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["JwtSettings:Key"] = TestJwt.Key,
            ["JwtSettings:Issuer"] = TestJwt.Issuer,
            ["JwtSettings:Audience"] = TestJwt.Audience,
            ["JwtSettings:ExpiryMinutes"] = "60",
        }));

        return base.CreateHost(builder);
    }
}
