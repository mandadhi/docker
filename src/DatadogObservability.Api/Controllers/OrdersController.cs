using DatadogObservability.Api.DTOs;
using DatadogObservability.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DatadogObservability.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(OrderService orders, ILogger<OrdersController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request, CancellationToken ct)
    {
        logger.LogInformation("Received create-order request for {Quantity} item(s)", request.Quantity);
        var order = await orders.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, new { orderId = order.Id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await orders.GetAsync(id, ct) is { } order ? Ok(order) : NotFound();

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await orders.ListAsync(ct));
}
