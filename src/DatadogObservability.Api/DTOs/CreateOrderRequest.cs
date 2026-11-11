using System.ComponentModel.DataAnnotations;

namespace DatadogObservability.Api.DTOs;

public class CreateOrderRequest
{
    public int? UserId { get; set; }

    [Required, StringLength(40, MinimumLength = 2)]
    public string ProductSku { get; set; } = string.Empty;

    [Range(1, 1000)]
    public int Quantity { get; set; }

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }
}
