using AuthGrpcService;
using BookingService;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace RoomBooking.Tests.Integration;

public class BookingApiFactory(string connectionString, Auth.AuthClient? authClient = null)
    : WebApplicationFactory<IApiMarker>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["JwtSettings:Key"] = TestJwt.Key,
            ["JwtSettings:Issuer"] = TestJwt.Issuer,
            ["JwtSettings:Audience"] = TestJwt.Audience,
            // Only dialled by /api/auth/*; those tests pass a stand-in client instead.
            ["AuthService:Address"] = "http://localhost:1",
        }));

        if (authClient is not null)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<Auth.AuthClient>();
                services.AddSingleton(authClient);
            });
        }

        return base.CreateHost(builder);
    }
}
