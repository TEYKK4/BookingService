using AuthGrpcService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthGrpcService.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
}
