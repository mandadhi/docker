using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DatadogObservability.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DatadogObservability.Tests;

public class FakeExternalApi : IExternalApiService
{
    public Task<ExternalCallResult> CallAsync(CancellationToken ct = default) =>
        Task.FromResult(new ExternalCallResult(true, 200, 1, "fake.local"));
}

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"dd-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_db}");
        builder.ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton<IExternalApiService, FakeExternalApi>()));
        // Replace() above is singleton; WorkflowService/Monitoring resolve it fine.
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_db); } catch { /* sqlite may still hold the file briefly */ }
    }
}

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_returns_status_environment_version_timestamp()
    {
        var res = await _client.GetAsync("/api/health");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", json.GetProperty("status").GetString());
        Assert.Equal("Testing", json.GetProperty("environment").GetString());
        Assert.True(json.TryGetProperty("version", out _));
        Assert.True(json.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task Live_and_ready_are_ok()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Create_order_then_get_it()
    {
        var res = await _client.PostAsJsonAsync("/api/orders", new { productSku = "SKU-T1", quantity = 2, amount = 10.5 });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid();

        var get = await _client.GetAsync($"/api/orders/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task Create_order_with_invalid_input_returns_400()
    {
        var res = await _client.PostAsJsonAsync("/api/orders", new { productSku = "", quantity = 0, amount = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_order_returns_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/orders/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Payment_succeeds_for_existing_order()
    {
        var o = await _client.PostAsJsonAsync("/api/orders", new { productSku = "SKU-P1", quantity = 1, amount = 5 });
        var orderId = (await o.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid();
        var res = await _client.PostAsJsonAsync("/api/payments", new { orderId, amount = 5, currency = "USD" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Succeeded", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Payment_failure_returns_sanitized_502_and_correlation_id()
    {
        var res = await _client.PostAsync("/api/payments/failure", null);
        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        Assert.True(res.Headers.Contains("X-Correlation-ID"));
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("pay-gw-01", body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("   at ", body);
    }

    [Fact]
    public async Task Monitoring_exception_returns_generic_500()
    {
        var res = await _client.GetAsync("/api/monitoring/exception");
        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.DoesNotContain("Controlled exception", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Incoming_correlation_id_is_echoed_and_bad_ones_replaced()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        req.Headers.Add("X-Correlation-ID", "abc-123");
        Assert.Equal("abc-123", (await _client.SendAsync(req)).Headers.GetValues("X-Correlation-ID").Single());

        var bad = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        bad.Headers.Add("X-Correlation-ID", "bad value\twith spaces");
        Assert.NotEqual("bad value\twith spaces", (await _client.SendAsync(bad)).Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task User_create_get_and_duplicate_conflict()
    {
        var body = new { name = "Test", email = $"t{Guid.NewGuid():N}@example.com" };
        var res = await _client.PostAsJsonAsync("/api/users", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/users/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/users", body)).StatusCode);
    }

    [Fact]
    public async Task Monitoring_endpoints_work()
    {
        foreach (var path in new[] { "/api/monitoring/logs", "/api/monitoring/database", "/api/monitoring/external-call", "/api/monitoring/workflow", "/api/monitoring/metrics" })
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/monitoring/slow?delayMs=50")).StatusCode);
    }
}
