using System.Collections.Concurrent;
using DatadogObservability.Api.Configuration;
using Microsoft.Extensions.Options;
using StatsdClient;

namespace DatadogObservability.Api.Observability;

/// <summary>
/// Custom application metrics (NOT collected automatically). Always kept in memory for the
/// /api/monitoring/metrics snapshot; sent to Datadog via DogStatsD only when DD_CUSTOM_METRICS_ENABLED=true.
/// </summary>
public sealed class CustomMetricsService : IDisposable
{
    public const string OrdersCreated = "demo.orders.created";
    public const string PaymentsProcessed = "demo.payments.processed";
    public const string PaymentsFailed = "demo.payments.failed";
    public const string RequestDuration = "demo.request.duration";
    public const string ExternalApiDuration = "demo.external_api.duration";
    public const string DbOperationDuration = "demo.db.operation.duration";

    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, (long Count, double SumMs)> _timings = new();
    private readonly DogStatsdService? _statsd;

    public CustomMetricsService(IOptions<DatadogOptions> options, ILogger<CustomMetricsService> logger)
    {
        var o = options.Value;
        if (!o.CustomMetricsEnabled) return;
        try
        {
            _statsd = new DogStatsdService();
            // Host/port are left to the client defaults: on App Service the extension's DogStatsD is
            // picked up automatically; elsewhere set DD_AGENT_HOST / DD_DOGSTATSD_PORT.
            _statsd.Configure(new StatsdConfig
            {
                ConstantTags = [$"service:{o.Service}", $"env:{o.Env}", $"version:{o.Version}"]
            });
            logger.LogInformation("DogStatsD custom metrics enabled");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DogStatsD could not be configured; custom metrics stay in-memory only");
            _statsd = null;
        }
    }

    public void Increment(string name)
    {
        _counters.AddOrUpdate(name, 1, (_, v) => v + 1);
        _statsd?.Increment(name);
    }

    public void RecordDuration(string name, TimeSpan elapsed, params string[] tags)
    {
        _timings.AddOrUpdate(name, (1, elapsed.TotalMilliseconds), (_, v) => (v.Count + 1, v.SumMs + elapsed.TotalMilliseconds));
        _statsd?.Distribution(name, elapsed.TotalMilliseconds, tags: tags);
    }

    public object Snapshot() => new
    {
        counters = _counters.ToDictionary(k => k.Key, k => k.Value),
        timings = _timings.ToDictionary(k => k.Key, k => new { count = k.Value.Count, avgMs = Math.Round(k.Value.SumMs / k.Value.Count, 2) }),
        sentToDatadog = _statsd is not null
    };

    public void Dispose() => _statsd?.Dispose();
}
