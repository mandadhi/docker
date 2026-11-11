using System.Diagnostics;
using DatadogObservability.Api.Observability;

namespace DatadogObservability.Api.Services;

public record ExternalCallResult(bool Success, int? StatusCode, long ElapsedMs, string Target);

public interface IExternalApiService
{
    Task<ExternalCallResult> CallAsync(CancellationToken ct = default);
}

/// <summary>
/// Outgoing HTTP via HttpClient: the profiler instruments it automatically (http.request span);
/// the surrounding ExternalApi.Call span is manual. Failures are reported, not thrown.
/// </summary>
public class ExternalApiService(HttpClient http, DatadogTracingService tracing, CustomMetricsService metrics,
    ILogger<ExternalApiService> logger) : IExternalApiService
{
    public Task<ExternalCallResult> CallAsync(CancellationToken ct = default) =>
        tracing.TraceAsync("ExternalApi.Call", "http", async span =>
        {
            var target = http.BaseAddress?.Host ?? "unconfigured";
            span.SetTag("peer.host", target);
            var sw = Stopwatch.StartNew();
            try
            {
                using var response = await http.GetAsync("/", ct);
                sw.Stop();
                metrics.RecordDuration(CustomMetricsService.ExternalApiDuration, sw.Elapsed, $"peer:{target}");
                logger.LogInformation("External call to {Target} returned {StatusCode} in {ElapsedMs} ms",
                    target, (int)response.StatusCode, sw.ElapsedMilliseconds);
                return new ExternalCallResult(response.IsSuccessStatusCode, (int)response.StatusCode, sw.ElapsedMilliseconds, target);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                sw.Stop();
                metrics.RecordDuration(CustomMetricsService.ExternalApiDuration, sw.Elapsed, $"peer:{target}");
                span.SetException(ex);
                logger.LogWarning(ex, "External call to {Target} failed after {ElapsedMs} ms", target, sw.ElapsedMilliseconds);
                return new ExternalCallResult(false, null, sw.ElapsedMilliseconds, target);
            }
        });
}
