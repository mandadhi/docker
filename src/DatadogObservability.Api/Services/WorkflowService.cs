using DatadogObservability.Api.DTOs;
using DatadogObservability.Api.Observability;

namespace DatadogObservability.Api.Services;

/// <summary>Multi-step workflow: Order -> Payment -> Database -> ExternalApi, all under one Workflow.Execute span.</summary>
public class WorkflowService(OrderService orders, PaymentService payments, DatabaseService database,
    IExternalApiService external, DatadogTracingService tracing, ILogger<WorkflowService> logger)
{
    public const string Name = "order-to-payment";

    public Task<object> ExecuteAsync(CancellationToken ct = default) =>
        tracing.TraceAsync("Workflow.Execute", "workflow", async span =>
        {
            logger.LogInformation("Workflow {WorkflowName} started", Name);

            var order = await orders.CreateAsync(new CreateOrderRequest { ProductSku = "WORKFLOW-SKU", Quantity = 1, Amount = 42.00m }, ct);
            var payment = await payments.ProcessAsync(new CreatePaymentRequest { OrderId = order.Id, Amount = order.Amount, Currency = "USD" }, ct);
            var db = await database.RunQueriesAsync(ct);
            var ext = await external.CallAsync(ct);

            logger.LogInformation("Workflow {WorkflowName} completed", Name);
            return (object)new { workflow = Name, orderId = order.Id, paymentId = payment.Id, database = db, externalCall = ext };
        }, new Dictionary<string, string?> { ["workflow.name"] = Name });
}
