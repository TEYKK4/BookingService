using Microsoft.EntityFrameworkCore;
using RoomBooking.Models;

namespace RoomBooking.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Login).HasMaxLength(50);

            // Stored as text ("User"/"Admin") rather than an int: readable in the
            // database, and reordering the enum can never silently change meaning.
            user.Property(u => u.Role)
                .HasConversion<string>()
                .HasMaxLength(16)
                .HasDefaultValue(UserRole.User);

            // The rule that actually prevents two accounts with one login. The
            // check in the handler only exists to produce a friendly error first.
            user.HasIndex(u => u.Login).IsUnique();
        });

        modelBuilder.Entity<Room>(room =>
        {
            room.Property(r => r.Name).HasMaxLength(100);
            room.Property(r => r.TimeZoneId).HasMaxLength(64);

            // Default lives in the database too, so the migration marks every
            // existing room active instead of the bool's implicit false.
            room.Property(r => r.IsActive).HasDefaultValue(true);

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

            // UserId is taken from the signed token, not from the request, and there
            // is no user-deletion flow, so it is stored as a plain column for now.
        });
    }
}
