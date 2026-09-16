using Microsoft.EntityFrameworkCore;
using RoomBooking.Models;

namespace RoomBooking.Data;

/// <summary>
/// Makes sure the account named in configuration exists and is an admin.
/// Runs at startup, after migrations. Idempotent: a second start changes nothing.
/// </summary>
public static class AdminSeeder
{
    public static async Task EnsureAdminAsync(AppDbContext db, AdminSettings settings, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(settings.Login) || string.IsNullOrWhiteSpace(settings.Password))
        {
            // Not fatal: tests and a bare dev run work without an admin. Loud, though.
            logger.LogWarning("Admin:Login / Admin:Password not set - no admin account will be created");
            return;
        }

        var admin = await db.Users.FirstOrDefaultAsync(u => u.Login == settings.Login);

        if (admin is null)
        {
            db.Users.Add(new User
            {
                Login = settings.Login,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(settings.Password),
                Role = UserRole.Admin,
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Admin account {Login} created", settings.Login);
            return;
        }

        if (admin.Role != UserRole.Admin)
        {
            admin.Role = UserRole.Admin;
            await db.SaveChangesAsync();
            logger.LogInformation("Existing user {Login} promoted to admin", settings.Login);
        }

        // The password is deliberately not reset for an existing account: the
        // configured value is a bootstrap secret, not a password-reset mechanism.
    }
}
