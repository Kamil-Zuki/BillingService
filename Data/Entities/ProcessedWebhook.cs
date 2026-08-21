namespace BillingService.Data.Entities;

public class ProcessedWebhook
{
    public BillingProvider Provider { get; set; }

    public string EventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; }

    public string PayloadHash { get; set; } = string.Empty;
}
