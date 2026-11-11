using DatadogObservability.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DatadogObservability.Api.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Order>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.CreatedAtUtc); e.Property(x => x.Amount).HasPrecision(18, 2); });
        b.Entity<Payment>(e => { e.HasKey(x => x.Id); e.Property(x => x.Amount).HasPrecision(18, 2); });
        b.Entity<User>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Email).IsUnique(); });
    }
}
