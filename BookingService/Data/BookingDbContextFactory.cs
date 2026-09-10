using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookingService.Data;

/// <summary>
/// Used only by `dotnet ef` at design time. It keeps migrations working without
/// starting the real app, so they do not depend on env vars being present.
/// </summary>
public class BookingDbContextFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql("Host=localhost;Database=bookingdb;Username=postgres;Password=postgres")
            .Options;

        return new BookingDbContext(options);
    }
}
