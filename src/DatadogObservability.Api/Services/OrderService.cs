using System.Diagnostics;
using DatadogObservability.Api.Data;
using DatadogObservability.Api.DTOs;
using DatadogObservability.Api.Models;
using DatadogObservability.Api.Observability;
using Microsoft.EntityFrameworkCore;

namespace DatadogObservability.Api.Services;

public class OrderService(ApplicationDbContext db, DatadogTracingService tracing, CustomMetricsService metrics,
    ILogger<OrderService> logger)
{
    public async Task<Order> CreateAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        using var root = tracing.Start("Order.Create", "business");
        try
        {
            await tracing.TraceAsync("Order.Validate", "validation", async _ =>
            {
                if (request.UserId is { } uid && !await db.Users.AnyAsync(u => u.Id == uid, ct))
                    throw new KeyNotFoundException($"User {uid} does not exist");
            });

            var order = new Order
            {
                UserId = request.UserId,
                ProductSku = request.ProductSku,
                Quantity = request.Quantity,
                Amount = request.Amount
            };

            await tracing.TraceAsync("Order.SaveToDatabase", "db", async _ =>
            {
                var sw = Stopwatch.StartNew();
                db.Orders.Add(order);
                await db.SaveChangesAsync(ct);
                metrics.RecordDuration(CustomMetricsService.DbOperationDuration, sw.Elapsed, "operation:order_save");
            });

            root.Span.SetTag("order.id", order.Id.ToString());
            root.Span.SetTag("operation.status", "success");
            DatadogTracingService.TagActive("order.id", order.Id.ToString());
            metrics.Increment(CustomMetricsService.OrdersCreated);
            logger.LogInformation("Order {OrderId} created for {Quantity} item(s)", order.Id, order.Quantity);
            return order;
        }
        catch (Exception ex)
        {
            root.Span.SetException(ex);
            root.Span.SetTag("operation.status", "error");
            throw;
        }
    }

    public Task<Order?> GetAsync(Guid id, CancellationToken ct = default) =>
        tracing.TraceAsync("Order.Get", "db", _ => db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct),
            new Dictionary<string, string?> { ["order.id"] = id.ToString() });

    public Task<List<Order>> ListAsync(CancellationToken ct = default) =>
        tracing.TraceAsync("Order.List", "db",
            _ => db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedAtUtc).Take(100).ToListAsync(ct));
}
