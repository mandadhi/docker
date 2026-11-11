# Datadog Observability Demo (ASP.NET Core / .NET 10)

A sample Web API built to generate realistic traffic and prove end-to-end observability in Datadog after deployment to **Azure App Service**: logs, traces, custom spans, database and HTTP dependencies, exceptions, runtime and custom metrics.

> **Verification status (be aware before relying on this):**
> - **Verified locally:** the solution builds, 11 integration tests pass, the app runs, every endpoint responds, the load script runs, and logs are structured JSON with correlation IDs.
> - **Not verified here:** deployment to Azure and anything that needs the Datadog profiler (automatic spans, `dd.trace_id` injection, error tracking, runtime metrics). That requires the Datadog extension in Azure. The Azure/Datadog steps below follow Datadog's current docs, but run the [verification checklist](#12-verification-checklist) before declaring success.

## 1. Architecture

```
Client -> ASP.NET Core API (Kestrel/IIS) -> Services -> EF Core -> SQLite
                |                               \-> HttpClient -> external API
                |-- Serilog JSON logs -> console + %HOME%\LogFiles\Application\app-*.log
                |-- Datadog.Trace manual spans/tags (children of the automatic request span)
                \-- DogStatsD custom metrics (optional)
Datadog profiler (from App Service extension) auto-instruments ASP.NET Core, HttpClient, ADO.NET/SQLite
```

```
src/DatadogObservability.Api/
  Controllers/    Health, Orders, Payments, Users, Monitoring (thin)
  Services/       Order, Payment, User, Database, ExternalApi, Workflow
  Data/           ApplicationDbContext, SeedData (EnsureCreated, no migrations: demo only)
  Middleware/     RequestLogging (correlation id, request log), ExceptionHandling (central, sanitized)
  Observability/  DatadogTracingService, CustomMetricsService, CorrelationService (+ Serilog enricher)
  Models/ DTOs/ Configuration/DatadogOptions.cs
tests/DatadogObservability.Tests/   xUnit integration tests (WebApplicationFactory)
scripts/load-test.ps1               concurrent traffic generator
.github/workflows/ci.yml, deploy.yml
```

## 2. Prerequisites

.NET 10 SDK; PowerShell (load script); an Azure subscription and a Datadog account for deployment.

## 3. Run locally

```powershell
dotnet run --project src/DatadogObservability.Api --urls http://localhost:5098
# Swagger UI: http://localhost:5098/swagger      Tests: dotnet test
```

Locally there is **no profiler**, so no traces are produced and the manual-span calls are no-ops. Logs and the app still work; `dd.trace_id` is absent from logs and the error response `traceId` falls back to the W3C trace ID. Logs go to `src/DatadogObservability.Api/logs/`, the SQLite DB to `App_Data/` (both git-ignored).

## 4. Endpoints

| Endpoint | Purpose |
|---|---|
| `GET /api/health`, `/ready`, `/live` | status/env/version; DB check (503 if down); process liveness |
| `POST /api/orders`, `GET /api/orders/{id}`, `GET /api/orders` | spans `Order.Create` > `Order.Validate`, `Order.SaveToDatabase` |
| `POST /api/payments` | `Payment.Process` > `Validate`, `GatewayCall`, `SaveTransaction` |
| `POST /api/payments/failure` | throws, logged with stack trace, trace flagged, sanitized **502** |
| `POST /api/users`, `GET /api/users/{id}` | DB spans (email/name never logged) |
| `GET /api/monitoring/logs` | Debug/Info/Warning/Error logs |
| `GET /api/monitoring/exception` | controlled exception, sanitized **500** |
| `GET /api/monitoring/slow?delayMs=` | delay (default 3000, capped by `Monitoring:MaxSlowDelayMs`) |
| `GET /api/monitoring/database` | 5 distinct queries |
| `GET /api/monitoring/external-call` | outgoing HttpClient call (`ExternalApi:BaseUrl`, default example.com) |
| `GET /api/monitoring/workflow` | `Workflow.Execute` > Order, Payment, Database, ExternalApi |
| `GET /api/monitoring/metrics` | in-memory custom-metric snapshot |

Custom span tags: `service.name`, `environment`, `operation.type`, `operation.status`, `order.id`, `payment.id`, `workflow.name`, plus `correlation.id` on the request span. IDs are per-entity, so avoid using them as metric tags.

## 5. NuGet packages: what each one does (and does not do)

| Package | Role |
|---|---|
| `Datadog.Trace` 3.57.0 | **Manual API only** (`Tracer.Instance.StartActive`, tags, `SetException`, `CorrelationIdentifier`). Installing it does **not** enable the profiler. |
| `Datadog.Trace.Bundle` | **Not used.** It ships the tracer binaries for the Linux/sidecar/container approach. Referencing it alongside the Windows extension would give you two tracers. Use `Datadog.Trace` *or* the Bundle, never both. |
| `DogStatsD-CSharp-Client` 9.2.1 | Custom metrics over DogStatsD. |
| `Serilog.AspNetCore` 10.0.0 | Structured logging (includes console/file sinks and compact JSON formatter). |
| `Serilog.Sinks.Datadog.Logs` 0.6.1 | Optional agentless log shipping; off unless `DD_LOGS_DIRECT_ENABLED=true`. |
| EF Core SQLite 10.0.15, Swashbuckle 10.3.1 | Data access and Swagger. |

## 6. Instrumentation architecture: who produces what

| Telemetry | Source |
|---|---|
| App Service platform metrics (CPU, memory, requests, HTTP 5xx), Activity/audit logs, App Service platform logs | **Azure Native Datadog integration** (resource metrics + diagnostic settings) |
| Tracer + profiler, DogStatsD listener, trace-agent, `DD_*` wiring | **Datadog App Service extension** (`Datadog.AzureAppServices.DotNet`) |
| `aspnet_core.request`, `http.request`, SQLite/ADO.NET spans, automatic APM metrics (`trace.*`), runtime metrics, `dd.trace_id` log injection, exception capture on spans | **Automatic instrumentation** (profiler from the extension) |
| `Order.*`, `Payment.*`, `Workflow.*`, `ExternalApi.Call`, `Database.*` spans and their tags | **Custom instrumentation** (`Datadog.Trace` in this app) |
| JSON logs with CorrelationId, path, method, status, duration | **Application logging** (Serilog) |
| `demo.*` metrics | **Custom metrics** (DogStatsD, this app) |

### Metric classification

| Metric | Type |
|---|---|
| `runtime.dotnet.*` (GC, heap, allocations, thread pool, exceptions), process metrics | Automatic **runtime** metrics: need the profiler/tracer and `DD_RUNTIME_METRICS_ENABLED=true`; on AAS they are sent via the extension's DogStatsD |
| `trace.aspnet_core.request.*` (hits, errors, duration) | Automatic **APM-generated** metrics |
| `demo.orders.created`, `demo.payments.processed`, `demo.payments.failed` | **Custom** counters |
| `demo.request.duration`, `demo.external_api.duration`, `demo.db.operation.duration`, `demo.payment.duration` | **Custom** distributions (ms) |

Custom metrics do **not** appear just because APM is on: set `DD_CUSTOM_METRICS_ENABLED=true` (an app-level switch, not a Datadog variable). Without it they stay in memory (`/api/monitoring/metrics`).

## 7. Recommended deployment approach

**Windows App Service + Datadog App Service extension** (supports .NET 8, 9 and 10 per Datadog docs). The extension supplies the profiler, so:

- Do **not** set `CORECLR_ENABLE_PROFILING`/`CORECLR_PROFILER*` yourself and do **not** deploy `Datadog.Trace.Bundle` or a second profiler: the extension owns them.
- Keep the `Datadog.Trace` NuGet only for custom spans. Datadog expects the NuGet version to be compatible with the extension's tracer: **keep them aligned** (check the tracer version the extension installed, then pin `Datadog.Trace` to the same or an older release). A newer NuGet than the profiler can silently disable manual spans. Re-check after extension updates.
- Do not set `DD_AGENT_HOST` / `DD_TRACE_AGENT_URL` (the extension manages them).

**Linux alternative:** Datadog's Linux path is the **sidecar** (`datadog/serverless-init`) with `Datadog.Trace.Bundle` and `CORECLR_*` variables (`DD_DOTNET_TRACER_HOME`, `CORECLR_PROFILER={846F5F1C-F9AE-4B07-969E-05C26BC060D8}`, `CORECLR_PROFILER_PATH=.../linux-x64/Datadog.Trace.ClrProfiler.Native.so`), and `DD_SERVERLESS_LOG_PATH=/home/LogFiles/*.log` for file logs. If you go that way, **replace** `Datadog.Trace` with `Datadog.Trace.Bundle` in the csproj (same API). Not tested here.

## 8. Azure setup steps

1. **Create the App Service:** OS **Windows**, Publish **Code**, Runtime stack **.NET 10 (LTS)**, a Basic+ plan (Free/Shared cannot run extensions reliably).
2. **Datadog resource:** Azure portal -> *Datadog* (Azure Native integration) -> Create (or link an existing Datadog org). Configure metric and resource-log collection for the subscription/resource group containing the app.
3. **Install the extension** (app must be **stopped** during the install/update): via the Datadog resource's App Service/extension blade, `datadog-ci aas instrument`, Bicep/Terraform, or Kudu site extensions (`Datadog.AzureAppServices.DotNet`). Exact blade names vary; use Datadog's current Azure App Service docs.
4. **Set application settings** (Section 9). Start the app.
5. **Deploy** with the GitHub Action (`deploy.yml`): create an Entra app/federated credential, then add `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` as secrets and `AZURE_WEBAPP_NAME` as a repository variable.
6. **Restart** the App Service, then run the load script against it.

## 9. Application settings

**Mandatory**

| Setting | Value / note |
|---|---|
| `DD_API_KEY` | Never in Git. Use a Key Vault reference `@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/dd-api-key/)` or let the Native integration set it |
| `DD_SITE` | e.g. `datadoghq.com`, `us5.datadoghq.com`, `datadoghq.eu`: must match your org |
| `DD_SERVICE` | `datadog-observability-demo` |
| `DD_ENV` | `development` (or your env) |
| `DD_VERSION` | `1.0.0` (set per release) |
| `DD_LOGS_INJECTION` | `true` (adds `dd.trace_id`/`dd.span_id` to Serilog events) |

**Recommended / optional**

| Setting | Value / note |
|---|---|
| `DD_TRACE_ENABLED` | `true` (default) |
| `DD_RUNTIME_METRICS_ENABLED` | `true` |
| `DD_CUSTOM_METRICS_ENABLED` | `true` to send `demo.*` metrics (app-specific) |
| `DD_LOGS_DIRECT_ENABLED` | `true` for agentless logs from the app (app-specific; requires `DD_API_KEY`) |
| `DD_TRACE_DEBUG` | `false`. Enable **only** briefly to troubleshoot; never in production |
| `ASPNETCORE_ENVIRONMENT` | `Production` (or `Development` to see Debug logs) |
| `Swagger__Enabled`, `ExternalApi__BaseUrl`, `Monitoring__MaxSlowDelayMs` | app behaviour |

APM and the profiler need no extra setting: the extension enables them. Tracer/extension logs are in `%HOME%\LogFiles\datadog`.

## 10. Log collection

The app writes compact JSON (`@t`, `@m`, `@l`, `@x`, plus `CorrelationId`, `RequestPath`, `HttpMethod`, `StatusCode`, `DurationMs`, `Application`, `Environment`, `dd.*`) to the console and to `%HOME%\LogFiles\Application\app-<date>.log`. Choose **one** path to Datadog to avoid duplicates:

1. **Agentless from the app (recommended on Windows):** set `DD_LOGS_DIRECT_ENABLED=true` + `DD_API_KEY` + `DD_SITE`. Datadog's Windows docs describe agentless logging (APM extension or Serilog sink) rather than file tailing.
2. **Azure diagnostic settings via the Native integration:** forwards App Service platform/HTTP/console logs. These are Azure's logs, not your Serilog JSON fields, so correlation fields won't be parsed the same way.

Check in Datadog that `CorrelationId` and `dd.trace_id` are facets/attributes; if the `dd.*` attribute names aren't mapped to the trace ID in your log pipeline, add a trace-ID remapper on `dd.trace_id`. Passwords, tokens, API keys, card data and PII (names/emails) are not logged by this code; the payment DTO has no card fields on purpose.

## 11. Test scenarios

Replace `$B` with `http://localhost:5098` or `https://<app>.azurewebsites.net`.

| # | Call | Expect in Datadog |
|---|---|---|
| 1 | `GET $B/api/orders` | request trace, logs, duration, 200 |
| 2 | `POST $B/api/orders` body `{"productSku":"SKU-9","quantity":1,"amount":9.5}` | `Order.Create` > `Validate`, `SaveToDatabase`, SQL span, correlated logs |
| 3 | `POST $B/api/payments/failure` | error span with stack trace, error logs, Error Tracking issue (502) |
| 4 | `GET $B/api/monitoring/slow?delayMs=3000` | ~3 s trace, warning log |
| 5 | `GET $B/api/monitoring/external-call` | `http.request` span under `ExternalApi.Call` |
| 6 | `GET $B/api/monitoring/workflow` | `Workflow.Execute` with Order, Payment, SQL and HTTP children |
| 7 | `./scripts/load-test.ps1 -BaseUrl $B -Rounds 20 -Concurrency 8` | mixed 2xx/4xx/5xx, slow and DB traffic |

## 12. Verification checklist

- [ ] App Service **Windows**, .NET 10, extension installed, app restarted.
- [ ] Datadog -> *Infrastructure/Serverless -> Azure App Service* shows the app (Native integration metrics).
- [ ] APM -> Services lists `datadog-observability-demo` with the right `env` and `version`.
- [ ] A trace for `GET /api/orders` shows `aspnet_core.request`; after `POST /api/orders` it also shows `Order.*` custom spans **nested under it** (if custom spans appear as separate traces or are missing, the NuGet/extension tracer versions are misaligned or the profiler isn't attached).
- [ ] SQL spans from SQLite appear under the order spans (if not, the manual `Order.SaveToDatabase` span still shows the timing; check the Datadog compatibility list for `Microsoft.Data.Sqlite`).
- [ ] `/api/monitoring/external-call` shows an outgoing `http.request`.
- [ ] `/api/payments/failure` creates an Error Tracking issue.
- [ ] Log search `service:datadog-observability-demo` returns JSON logs; opening a trace shows its **Logs** tab populated.
- [ ] Metrics Explorer: `runtime.dotnet.gc.*`, `trace.aspnet_core.request.hits`, and (if enabled) `demo.orders.created`.
- [ ] `DD_TRACE_DEBUG` is `false`; no secrets in Git or workflow files.

## 13. Troubleshooting

| Symptom | Check |
|---|---|
| No traces | App fully stopped/started after extension install; extension version; `%HOME%\LogFiles\datadog` logs; `DD_TRACE_ENABLED`; `DD_SITE`/`DD_API_KEY` correct |
| Auto spans present, custom spans missing | `Datadog.Trace` NuGet newer than the extension's tracer: align versions |
| Duplicate/odd traces | Two tracers installed (Bundle + extension) or manual `CORECLR_*` set: remove them |
| Logs without trace IDs | `DD_LOGS_INJECTION=true`; profiler attached; the app's own enricher only adds IDs when a span is active |
| Logs missing | Only one log path active? `DD_LOGS_DIRECT_ENABLED` + `DD_API_KEY` set? Check outbound access to `http-intake.logs.<site>` |
| No `demo.*` metrics | `DD_CUSTOM_METRICS_ENABLED=true`; check `/api/monitoring/metrics` -> `sentToDatadog` |
| Data lost after restart | SQLite lives under `%HOME%\App_Data` (persistent); with multiple instances each has its own file: demo only |

Note: `ExceptionHandlingMiddleware` explicitly marks the active request span as errored, because the framework never sees exceptions the middleware catches.
