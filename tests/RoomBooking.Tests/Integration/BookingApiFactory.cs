using Microsoft.AspNetCore.Mvc.Testing;
using BookingService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace RoomBooking.Tests.Integration;

public class BookingApiFactory(string connectionString) : WebApplicationFactory<IApiMarker>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["JwtSettings:Key"] = TestJwt.Key,
            ["JwtSettings:Issuer"] = TestJwt.Issuer,
            ["JwtSettings:Audience"] = TestJwt.Audience,
            // Never dialled in these tests - booking endpoints verify tokens locally.
            ["AuthService:Address"] = "http://localhost:1",
        }));

        return base.CreateHost(builder);
    }
}
