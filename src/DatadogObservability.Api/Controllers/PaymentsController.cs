using DatadogObservability.Api.DTOs;
using DatadogObservability.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DatadogObservability.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController(PaymentService payments) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreatePaymentRequest request, CancellationToken ct)
    {
        var p = await payments.ProcessAsync(request, ct);
        return Ok(new { paymentId = p.Id, status = p.Status, processingMs = p.ProcessingMs });
    }

    /// <summary>Always fails: the exception middleware logs it, flags the trace and returns a sanitized 502.</summary>
    [HttpPost("failure")]
    public async Task<IActionResult> Failure(CancellationToken ct)
    {
        await payments.SimulateFailureAsync(ct);
        return Ok(); // unreachable
    }
}
