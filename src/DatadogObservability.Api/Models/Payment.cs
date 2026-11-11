namespace DatadogObservability.Api.Models;

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "Pending";
    public long ProcessingMs { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
