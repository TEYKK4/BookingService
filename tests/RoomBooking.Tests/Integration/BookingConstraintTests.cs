using RoomBooking.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace RoomBooking.Tests.Integration;

/// <summary>
/// Goes around the API on purpose. The concurrency test through HTTP can pass
/// on the handler's own check alone - this one proves the unique index is
/// really in the database, which is what holds when the check loses the race.
/// </summary>
public class BookingConstraintTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(postgres.ConnectionString)
        .Options);

    public async Task InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_database_refuses_two_bookings_for_one_room_and_slot()
    {
        var slot = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(200).AddHours(9), DateTimeKind.Utc);
        await using var db = NewDb();

        db.Bookings.Add(new RoomBooking.Models.Booking
        {
            RoomId = 1, UserId = 1, SlotStart = slot, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        db.Bookings.Add(new RoomBooking.Models.Booking
        {
            RoomId = 1, UserId = 2, SlotStart = slot, CreatedAt = DateTime.UtcNow,
        });

        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        exception.InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

}
