using BookingService.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Data;

public class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>(room =>
        {
            room.Property(r => r.Name).HasMaxLength(100);
            room.Property(r => r.TimeZoneId).HasMaxLength(64);

            // Rooms sit in different offices on purpose: it keeps the code honest
            // about working hours being local to the room, not to the server.
            room.HasData(
                new Room { Id = 1, Name = "Focus", Capacity = 2, TimeZoneId = "Europe/Warsaw" },
                new Room { Id = 2, Name = "Huddle", Capacity = 6, TimeZoneId = "Europe/Warsaw" },
                new Room { Id = 3, Name = "Boardroom", Capacity = 14, TimeZoneId = "Europe/London" },
                new Room { Id = 4, Name = "Training", Capacity = 30, TimeZoneId = "America/New_York" });
        });

        modelBuilder.Entity<Booking>(booking =>
        {
            // The rule that actually prevents double booking. The check in the
            // handler is only there to produce a friendly error first.
            booking.HasIndex(b => new { b.RoomId, b.SlotStart }).IsUnique();

            booking.HasIndex(b => b.UserId);

            booking.HasOne(b => b.Room)
                .WithMany(r => r.Bookings)
                .HasForeignKey(b => b.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
