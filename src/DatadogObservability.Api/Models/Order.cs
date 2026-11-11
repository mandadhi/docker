namespace DatadogObservability.Api.Models;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? UserId { get; set; }
    public string ProductSku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Created";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
