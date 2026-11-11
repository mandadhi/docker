using DatadogObservability.Api.Observability;
using DatadogObservability.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DatadogObservability.Api.Controllers;

[ApiController]
[Route("api/monitoring")]
public class MonitoringController(ILogger<MonitoringController> logger, IConfiguration config, DatabaseService database,
    IExternalApiService external, WorkflowService workflow, CustomMetricsService metrics) : ControllerBase
{
    [HttpGet("logs")]
    public IActionResult Logs()
    {
        logger.LogDebug("Sample DEBUG log (only visible when Serilog minimum level is Debug)");
        logger.LogInformation("Sample INFORMATION log with structured property {Feature}", "monitoring-logs");
        logger.LogWarning("Sample WARNING log with structured property {Threshold}", 80);
        logger.LogError("Sample ERROR log (no exception attached) with {ErrorCode}", "DEMO-001");
        return Ok(new { emitted = new[] { "Debug", "Information", "Warning", "Error" } });
    }

    [HttpGet("exception")]
    public IActionResult ThrowException()
    {
        logger.LogInformation("About to throw a controlled exception");
        throw new InvalidOperationException("Controlled exception for Datadog Error Tracking demo");
    }

    /// <param name="delayMs">Optional delay; clamped to Monitoring:MaxSlowDelayMs. Defaults to Monitoring:SlowDefaultMs.</param>
    [HttpGet("slow")]
    public async Task<IActionResult> Slow([FromQuery] int? delayMs, CancellationToken ct)
    {
        var max = config.GetValue("Monitoring:MaxSlowDelayMs", 30_000);
        var delay = Math.Clamp(delayMs ?? config.GetValue("Monitoring:SlowDefaultMs", 3_000), 0, max);
        logger.LogWarning("Slow request: sleeping for {DelayMs} ms", delay);
        await Task.Delay(delay, ct);
        return Ok(new { delayedMs = delay });
    }

    [HttpGet("database")]
    public async Task<IActionResult> Database(CancellationToken ct) => Ok(await database.RunQueriesAsync(ct));

    [HttpGet("external-call")]
    public async Task<IActionResult> ExternalCall(CancellationToken ct) => Ok(await external.CallAsync(ct));

    [HttpGet("workflow")]
    public async Task<IActionResult> Workflow(CancellationToken ct) => Ok(await workflow.ExecuteAsync(ct));

    /// <summary>In-memory view of the custom metrics (Datadog receives them via DogStatsD when enabled).</summary>
    [HttpGet("metrics")]
    public IActionResult Metrics() => Ok(metrics.Snapshot());
}
