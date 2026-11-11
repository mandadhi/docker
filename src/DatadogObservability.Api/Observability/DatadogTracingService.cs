using Datadog.Trace;
using DatadogObservability.Api.Configuration;
using Microsoft.Extensions.Options;

namespace DatadogObservability.Api.Observability;

/// <summary>
/// Thin wrapper over the Datadog.Trace manual API. Custom spans become children of whatever span is
/// currently active (the automatic aspnet_core.request span when the profiler is attached).
/// Without the profiler (e.g. plain local run) the calls are harmless no-ops.
/// </summary>
public class DatadogTracingService(IOptions<DatadogOptions> options)
{
    private readonly DatadogOptions _options = options.Value;

    public IScope Start(string operationName, string operationType, IDictionary<string, string?>? tags = null)
    {
        var scope = Tracer.Instance.StartActive(operationName);
        var span = scope.Span;
        span.ResourceName = operationName;
        span.SetTag("service.name", _options.Service);
        span.SetTag("environment", _options.Env);
        span.SetTag("operation.type", operationType);
        if (tags is not null)
            foreach (var (k, v) in tags)
                if (v is not null) span.SetTag(k, v);
        return scope;
    }

    public async Task<T> TraceAsync<T>(string operationName, string operationType, Func<ISpan, Task<T>> action,
        IDictionary<string, string?>? tags = null)
    {
        using var scope = Start(operationName, operationType, tags);
        try
        {
            var result = await action(scope.Span);
            scope.Span.SetTag("operation.status", "success");
            return result;
        }
        catch (Exception ex)
        {
            scope.Span.SetException(ex);
            scope.Span.SetTag("operation.status", "error");
            throw;
        }
    }

    public async Task TraceAsync(string operationName, string operationType, Func<ISpan, Task> action,
        IDictionary<string, string?>? tags = null) =>
        await TraceAsync<object?>(operationName, operationType, async s => { await action(s); return null; }, tags);

    /// <summary>Marks the currently active span (e.g. the request root span) as errored.</summary>
    public static void MarkActiveSpanError(Exception ex)
    {
        var span = Tracer.Instance.ActiveScope?.Span;
        span?.SetException(ex);
    }

    /// <summary>Tags the currently active span, e.g. to attach order.id to the request span.</summary>
    public static void TagActive(string key, string? value)
    {
        if (value is not null) Tracer.Instance.ActiveScope?.Span.SetTag(key, value);
    }
}
