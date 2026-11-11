using DatadogObservability.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DatadogObservability.Api.Data;

public static class SeedData
{
    /// <summary>Demo only: EnsureCreated instead of migrations keeps the sample dependency-free.</summary>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (await db.Users.AnyAsync()) return;

        var user = new User { Name = "Demo User", Email = "demo.user@example.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.Orders.AddRange(
            new Order { UserId = user.Id, ProductSku = "SKU-001", Quantity = 1, Amount = 19.99m },
            new Order { UserId = user.Id, ProductSku = "SKU-002", Quantity = 3, Amount = 74.50m });
        await db.SaveChangesAsync();
    }
}
