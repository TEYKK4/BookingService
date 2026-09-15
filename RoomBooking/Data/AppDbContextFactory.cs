using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace RoomBooking.Data;

/// <summary>
/// Used only by <c>dotnet ef</c> at design time. Resolves configuration in the
/// same order as the app - appsettings, then the environment-specific file, then
/// environment variables - so there is no second place with its own rules.
/// In practice appsettings leaves the connection string empty on purpose (secrets
/// do not go in git), so the value comes from <c>ConnectionStrings__DefaultConnection</c>.
/// Commands that only build the model (<c>migrations add</c>) work without it;
/// commands that touch a database need it set in the shell.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
            .Options;

        return new AppDbContext(options);
    }
}
