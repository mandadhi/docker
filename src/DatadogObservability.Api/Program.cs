using DatadogObservability.Api.Configuration;
using DatadogObservability.Api.Data;
using DatadogObservability.Api.Middleware;
using DatadogObservability.Api.Observability;
using DatadogObservability.Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Datadog.Logs;

var builder = WebApplication.CreateBuilder(args);

// ---- Datadog options (DD_* env vars win over appsettings) ----
var ddOptions = new DatadogOptions();
builder.Configuration.GetSection(DatadogOptions.SectionName).Bind(ddOptions);
DatadogOptions.ApplyEnvironment(ddOptions, builder.Configuration);
builder.Services.Configure<DatadogOptions>(o =>
{
    builder.Configuration.GetSection(DatadogOptions.SectionName).Bind(o);
    DatadogOptions.ApplyEnvironment(o, builder.Configuration);
});

// ---- Serilog: structured JSON to console + rolling file under %HOME%\LogFiles\Application ----
// Azure App Service sets HOME (D:\home on Windows, /home on Linux). Locally logs go to ./logs.
// Only treat HOME as the App Service home when actually running on App Service (WEBSITE_SITE_NAME is set there);
// developer shells also define HOME.
var home = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is null ? null : Environment.GetEnvironmentVariable("HOME");
var logDir = home is { Length: > 0 } ? Path.Combine(home, "LogFiles", "Application") : Path.Combine(builder.Environment.ContentRootPath, "logs");

builder.Host.UseSerilog((ctx, services, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()
      .Enrich.WithProperty("Application", ddOptions.Service)
      .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
      .Enrich.With(new DatadogTraceEnricher(ddOptions.Service, ddOptions.Env, ddOptions.Version))
      .WriteTo.Console(new RenderedCompactJsonFormatter())
      .WriteTo.File(new RenderedCompactJsonFormatter(), Path.Combine(logDir, "app-.log"),
          rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true);

    // Optional agentless shipping. Do NOT combine with an Agent that also tails the file (duplicates).
    var apiKey = ctx.Configuration["DD_API_KEY"];
    if (ddOptions.LogsDirectEnabled && !string.IsNullOrWhiteSpace(apiKey))
    {
        lc.WriteTo.DatadogLogs(apiKey, source: "csharp", service: ddOptions.Service,
            host: Environment.MachineName,
            tags: [$"env:{ddOptions.Env}", $"version:{ddOptions.Version}"],
            configuration: new DatadogConfiguration(url: $"https://http-intake.logs.{ddOptions.Site}"));
    }
});

// ---- Persistence (SQLite). On App Service keep the DB under HOME (persistent, writable). ----
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    var dataDir = Path.Combine(home is { Length: > 0 } ? home : builder.Environment.ContentRootPath, "App_Data");
    Directory.CreateDirectory(dataDir);
    connectionString = $"Data Source={Path.Combine(dataDir, "datadog-demo.db")}";
}
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connectionString));

// ---- Application services ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<CustomMetricsService>();
builder.Services.AddSingleton<DatadogTracingService>();
builder.Services.AddScoped<CorrelationService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<DatabaseService>();
builder.Services.AddScoped<WorkflowService>();
builder.Services.AddHttpClient<IExternalApiService, ExternalApiService>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["ExternalApi:BaseUrl"] ?? "https://example.com");
    c.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("ExternalApi:TimeoutSeconds", 10));
    c.DefaultRequestHeaders.UserAgent.ParseAdd("datadog-observability-demo/1.0");
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Order matters: request logging wraps exception handling so the final status (e.g. 500) is logged.
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Configuration.GetValue("Swagger:Enabled", true))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger"));

await SeedData.InitializeAsync(app.Services);

Log.Information("Starting {Service} {Version} in {Env} (Datadog trace enabled flag: {TraceEnabled}, logs injection: {LogsInjection}, logs written to {LogDir})",
    ddOptions.Service, ddOptions.Version, ddOptions.Env, ddOptions.TraceEnabled, ddOptions.LogsInjection, logDir);

app.Run();

public partial class Program;
