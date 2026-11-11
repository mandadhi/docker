namespace DatadogObservability.Api.Configuration;

/// <summary>
/// Mirrors the Datadog settings the app cares about. Values are read from the standard DD_* environment
/// variables first (App Service application settings are exposed as env vars), then from the "Datadog"
/// section of appsettings.json. These values drive log enrichment and span tags only: the tracer itself
/// reads DD_* directly from the environment.
/// </summary>
public class DatadogOptions
{
    public const string SectionName = "Datadog";

    public string Service { get; set; } = "datadog-observability-demo";
    public string Env { get; set; } = "development";
    public string Version { get; set; } = "1.0.0";
    public bool TraceEnabled { get; set; } = true;
    public bool LogsInjection { get; set; } = true;
    public bool RuntimeMetricsEnabled { get; set; } = true;
    public bool TraceDebug { get; set; }

    /// <summary>App-specific (not a Datadog variable): send custom metrics through DogStatsD.</summary>
    public bool CustomMetricsEnabled { get; set; }

    /// <summary>App-specific: ship logs agentlessly via the Datadog HTTP intake (needs DD_API_KEY).</summary>
    public bool LogsDirectEnabled { get; set; }

    public string Site { get; set; } = "datadoghq.com";

    public static void ApplyEnvironment(DatadogOptions o, IConfiguration c)
    {
        o.Service = c["DD_SERVICE"] ?? o.Service;
        o.Env = c["DD_ENV"] ?? o.Env;
        o.Version = c["DD_VERSION"] ?? o.Version;
        o.Site = c["DD_SITE"] ?? o.Site;
        o.TraceEnabled = Bool(c["DD_TRACE_ENABLED"], o.TraceEnabled);
        o.LogsInjection = Bool(c["DD_LOGS_INJECTION"], o.LogsInjection);
        o.RuntimeMetricsEnabled = Bool(c["DD_RUNTIME_METRICS_ENABLED"], o.RuntimeMetricsEnabled);
        o.TraceDebug = Bool(c["DD_TRACE_DEBUG"], o.TraceDebug);
        o.CustomMetricsEnabled = Bool(c["DD_CUSTOM_METRICS_ENABLED"], o.CustomMetricsEnabled);
        o.LogsDirectEnabled = Bool(c["DD_LOGS_DIRECT_ENABLED"], o.LogsDirectEnabled);
    }

    private static bool Bool(string? v, bool fallback) =>
        bool.TryParse(v, out var b) ? b : v == "1" ? true : v == "0" ? false : fallback;
}
