using System.Diagnostics;
using DatadogObservability.Api.Data;
using DatadogObservability.Api.Observability;
using Microsoft.EntityFrameworkCore;

namespace DatadogObservability.Api.Services;

public class DatabaseService(ApplicationDbContext db, DatadogTracingService tracing, CustomMetricsService metrics,
    ILogger<DatabaseService> logger)
{
    public async Task<bool> PingAsync(CancellationToken ct = default) => await db.Database.CanConnectAsync(ct);

    /// <summary>Runs several different queries so each shows up as its own SQL span.</summary>
    public async Task<object> RunQueriesAsync(CancellationToken ct = default)
    {
        return await tracing.TraceAsync("Database.RunQueries", "db", async _ =>
        {
            var sw = Stopwatch.StartNew();
            var users = await db.Users.AsNoTracking().CountAsync(ct);
            var orders = await db.Orders.AsNoTracking().CountAsync(ct);
            var payments = await db.Payments.AsNoTracking().CountAsync(ct);
            var latest = await db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedAtUtc).Take(5)
                .Select(o => new { o.Id, o.Status }).ToListAsync(ct);
            var hasPending = await db.Payments.AsNoTracking().AnyAsync(p => p.Status == "Pending", ct);
            sw.Stop();

            metrics.RecordDuration(CustomMetricsService.DbOperationDuration, sw.Elapsed, "operation:run_queries");
            logger.LogInformation("Executed 5 database queries in {ElapsedMs} ms", sw.ElapsedMilliseconds);
            return (object)new { users, orders, payments, latestOrders = latest, hasPending, elapsedMs = sw.ElapsedMilliseconds };
        }, new Dictionary<string, string?> { ["db.queries"] = "5" });
    }
}
