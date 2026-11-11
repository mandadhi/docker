using System.Reflection;
using DatadogObservability.Api.Configuration;
using DatadogObservability.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DatadogObservability.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController(IWebHostEnvironment env, IOptions<DatadogOptions> dd, DatabaseService database,
    ILogger<HealthController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "Healthy",
        environment = env.EnvironmentName,
        version = dd.Value.Version != "1.0.0" ? dd.Value.Version
            : Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0],
        timestamp = DateTimeOffset.UtcNow
    });

    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "Alive", timestamp = DateTimeOffset.UtcNow });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        var dbOk = false;
        try { dbOk = await database.PingAsync(ct); }
        catch (Exception ex) { logger.LogError(ex, "Readiness check: database unreachable"); }

        var body = new { status = dbOk ? "Ready" : "NotReady", checks = new { database = dbOk ? "Healthy" : "Unhealthy" }, timestamp = DateTimeOffset.UtcNow };
        return dbOk ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }
}
