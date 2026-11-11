using DatadogObservability.Api.Data;
using DatadogObservability.Api.DTOs;
using DatadogObservability.Api.Models;
using DatadogObservability.Api.Observability;
using Microsoft.EntityFrameworkCore;

namespace DatadogObservability.Api.Services;

public class UserService(ApplicationDbContext db, DatadogTracingService tracing, ILogger<UserService> logger)
{
    public Task<User> CreateAsync(CreateUserRequest request, CancellationToken ct = default) =>
        tracing.TraceAsync("User.Create", "db", async span =>
        {
            if (await db.Users.AnyAsync(u => u.Email == request.Email, ct))
                throw new InvalidOperationException("A user with this email already exists");

            var user = new User { Name = request.Name, Email = request.Email };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            span.SetTag("user.id", user.Id.ToString());
            // Never log the email/name: personal data.
            logger.LogInformation("User {UserId} created", user.Id);
            return user;
        });

    public Task<User?> GetAsync(int id, CancellationToken ct = default) =>
        tracing.TraceAsync("User.Get", "db", _ => db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct),
            new Dictionary<string, string?> { ["user.id"] = id.ToString() });
}
