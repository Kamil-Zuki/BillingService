namespace BillingService.Providers.Models;

public record WebhookHandleResult(IReadOnlyList<DomainEvent> Events)
{
    public static WebhookHandleResult Empty { get; } = new([]);
}
