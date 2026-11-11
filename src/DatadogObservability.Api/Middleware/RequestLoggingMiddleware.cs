using System.Diagnostics;
using DatadogObservability.Api.Observability;
using Serilog.Context;

namespace DatadogObservability.Api.Middleware;

/// <summary>
/// Assigns/propagates the correlation ID, pushes request context into Serilog, and writes one
/// structured completion log per request (path, method, status, duration). Query strings are not logged.
/// </summary>
public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, CustomMetricsService metrics)
    {
        var correlationId = CorrelationService.Resolve(context.Request.Headers[CorrelationService.HeaderName]);
        context.Items[CorrelationService.ItemKey] = correlationId;
        context.Response.Headers[CorrelationService.HeaderName] = correlationId;
        DatadogTracingService.TagActive("correlation.id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath", context.Request.Path.Value))
        using (LogContext.PushProperty("HttpMethod", context.Request.Method))
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await next(context);
            }
            finally
            {
                sw.Stop();
                var status = context.Response.StatusCode;
                metrics.RecordDuration(CustomMetricsService.RequestDuration, sw.Elapsed, $"method:{context.Request.Method}", $"status:{status}");
                using (LogContext.PushProperty("StatusCode", status))
                using (LogContext.PushProperty("DurationMs", sw.ElapsedMilliseconds))
                {
                    logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Information,
                        "HTTP {HttpMethod:l} {RequestPath:l} responded {StatusCode} in {DurationMs} ms",
                        context.Request.Method, context.Request.Path.Value, status, sw.ElapsedMilliseconds);
                }
            }
        }
    }
}
