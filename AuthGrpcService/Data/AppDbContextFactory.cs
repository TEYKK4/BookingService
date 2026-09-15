using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AuthGrpcService.Data;

/// <summary>
/// Used only by <c>dotnet ef</c> at design time. Reads the same configuration
/// sources as the app - appsettings and environment variables - so there is
/// nothing hardcoded here and no second place where a connection string lives.
/// Commands that only build the model (<c>migrations add</c>) work with no
/// connection string at all; commands that touch a database need
/// <c>ConnectionStrings__DefaultConnection</c> set in the shell.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
            .Options;

        return new AppDbContext(options);
    }
}
