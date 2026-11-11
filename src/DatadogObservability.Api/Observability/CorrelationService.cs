using Datadog.Trace;
using Serilog.Core;
using Serilog.Events;

namespace DatadogObservability.Api.Observability;

/// <summary>Holds the per-request correlation ID (X-Correlation-ID header or generated).</summary>
public class CorrelationService(IHttpContextAccessor accessor)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    public string CorrelationId =>
        accessor.HttpContext?.Items[ItemKey] as string ?? Guid.NewGuid().ToString("N");

    /// <summary>Accepts a caller-supplied ID only if it is short and benign; otherwise generates one.</summary>
    public static string Resolve(string? incoming) =>
        !string.IsNullOrWhiteSpace(incoming) && incoming.Length <= 64 && incoming.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            ? incoming
            : Guid.NewGuid().ToString("N");
}

/// <summary>
/// Adds dd.trace_id / dd.span_id / dd.service / dd.env / dd.version to every log event.
/// DD_LOGS_INJECTION=true makes the profiler inject the same keys; this enricher guarantees they exist
/// for the file/console JSON logs as well (it never overrides an injected value).
/// </summary>
public class DatadogTraceEnricher(string service, string env, string version) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        logEvent.AddPropertyIfAbsent(factory.CreateProperty("dd.service", service));
        logEvent.AddPropertyIfAbsent(factory.CreateProperty("dd.env", env));
        logEvent.AddPropertyIfAbsent(factory.CreateProperty("dd.version", version));

        var traceId = CorrelationIdentifier.TraceId;
        var spanId = CorrelationIdentifier.SpanId;
        if (traceId != 0 && spanId != 0)
        {
            logEvent.AddPropertyIfAbsent(factory.CreateProperty("dd.trace_id", traceId.ToString()));
            logEvent.AddPropertyIfAbsent(factory.CreateProperty("dd.span_id", spanId.ToString()));
        }
    }
}
