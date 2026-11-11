using Datadog.Trace;
using DatadogObservability.Api.Observability;
using DatadogObservability.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DatadogObservability.Api.Middleware;

/// <summary>
/// Central exception handler. Logs the full exception (with stack trace) and marks the active
/// request span as errored so it appears in Datadog Error Tracking. The response never contains
/// exception messages or stack traces.
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499; // client closed request
        }
        catch (Exception ex)
        {
            var (status, title) = ex switch
            {
                KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
                InvalidOperationException when ex.Message.Contains("already exists") => (StatusCodes.Status409Conflict, "Conflict"),
                PaymentGatewayException => (StatusCodes.Status502BadGateway, "Payment gateway error"),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
            };

            // Expected 4xx are warnings; real failures are errors and flag the trace.
            if (status >= 500)
            {
                logger.LogError(ex, "Unhandled exception processing {HttpMethod:l} {RequestPath:l}", context.Request.Method, context.Request.Path.Value);
                DatadogTracingService.MarkActiveSpanError(ex);
            }
            else
            {
                logger.LogWarning("Request rejected with {StatusCode}: {ExceptionType}", status, ex.GetType().Name);
            }

            if (context.Response.HasStarted) throw;

            context.Response.Clear(); // also drops headers, so restore the correlation ID
            if (context.Items[CorrelationService.ItemKey] is string cid)
                context.Response.Headers[CorrelationService.HeaderName] = cid;
            context.Response.StatusCode = status;
            await Results.Json(new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions =
                {
                    // Datadog trace ID when the profiler is attached, otherwise the W3C trace ID.
                    ["traceId"] = CorrelationIdentifier.TraceId != 0
                        ? CorrelationIdentifier.TraceId.ToString()
                        : System.Diagnostics.Activity.Current?.TraceId.ToString(),
                    ["correlationId"] = context.Items[CorrelationService.ItemKey]
                }
            }, contentType: "application/problem+json").ExecuteAsync(context);
        }
    }
}
