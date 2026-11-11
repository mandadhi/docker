using System.ComponentModel.DataAnnotations;

namespace DatadogObservability.Api.DTOs;

/// <summary>Deliberately carries no card data: never send PANs/CVVs to this demo.</summary>
public class CreatePaymentRequest
{
    public Guid? OrderId { get; set; }

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }

    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO code.")]
    public string Currency { get; set; } = "USD";
}
