using System.Diagnostics;
using DatadogObservability.Api.Data;
using DatadogObservability.Api.DTOs;
using DatadogObservability.Api.Models;
using DatadogObservability.Api.Observability;
using Microsoft.EntityFrameworkCore;

namespace DatadogObservability.Api.Services;

/// <summary>Raised when the (simulated) payment gateway fails. Message may hold internal details: never return it to clients.</summary>
public class PaymentGatewayException(string message, Exception? inner = null) : Exception(message, inner);

public class PaymentService(ApplicationDbContext db, DatadogTracingService tracing, CustomMetricsService metrics,
    ILogger<PaymentService> logger)
{
    public async Task<Payment> ProcessAsync(CreatePaymentRequest request, CancellationToken ct = default)
    {
        using var root = tracing.Start("Payment.Process", "business");
        var sw = Stopwatch.StartNew();
        var payment = new Payment { OrderId = request.OrderId, Amount = request.Amount, Currency = request.Currency };
        try
        {
            await tracing.TraceAsync("Payment.Validate", "validation", async _ =>
            {
                if (request.OrderId is { } oid && !await db.Orders.AnyAsync(o => o.Id == oid, ct))
                    throw new KeyNotFoundException($"Order {oid} does not exist");
            });

            await tracing.TraceAsync("Payment.GatewayCall", "http.simulated", async _ =>
            {
                // Simulated gateway latency (50-200 ms).
                await Task.Delay(Random.Shared.Next(50, 200), ct);
            }, new Dictionary<string, string?> { ["gateway"] = "simulated" });

            payment.Status = "Succeeded";
            payment.ProcessingMs = sw.ElapsedMilliseconds;

            await tracing.TraceAsync("Payment.SaveTransaction", "db", async _ =>
            {
                db.Payments.Add(payment);
                await db.SaveChangesAsync(ct);
            });

            root.Span.SetTag("payment.id", payment.Id.ToString());
            root.Span.SetTag("operation.status", "success");
            DatadogTracingService.TagActive("payment.id", payment.Id.ToString());
            metrics.Increment(CustomMetricsService.PaymentsProcessed);
            logger.LogInformation("Payment {PaymentId} succeeded in {ProcessingMs} ms", payment.Id, payment.ProcessingMs);
            return payment;
        }
        catch (Exception ex)
        {
            metrics.Increment(CustomMetricsService.PaymentsFailed);
            root.Span.SetException(ex);
            root.Span.SetTag("operation.status", "error");
            // The stack trace is logged once, by ExceptionHandlingMiddleware, to avoid duplicate error events.
            logger.LogWarning("Payment processing failed after {ProcessingMs} ms ({ExceptionType})", sw.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
        finally
        {
            metrics.RecordDuration("demo.payment.duration", sw.Elapsed);
        }
    }

    /// <summary>Intentionally fails to demonstrate exception tracking.</summary>
    public async Task SimulateFailureAsync(CancellationToken ct = default)
    {
        using var root = tracing.Start("Payment.Process", "business", new Dictionary<string, string?> { ["payment.scenario"] = "failure" });
        try
        {
            await tracing.TraceAsync("Payment.GatewayCall", "http.simulated", async _ =>
            {
                await Task.Delay(75, ct);
                throw new PaymentGatewayException("Simulated gateway timeout contacting payments upstream (internal host pay-gw-01:8443)");
            });
        }
        catch (Exception ex)
        {
            metrics.Increment(CustomMetricsService.PaymentsFailed);
            root.Span.SetException(ex);
            root.Span.SetTag("operation.status", "error");
            logger.LogWarning("Simulated payment failure ({ExceptionType})", ex.GetType().Name);
            throw;
        }
    }
}
