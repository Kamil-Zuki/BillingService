using BillingService.Providers.Models;

namespace BillingService.Services;

public interface IWebhookOrchestrator
{
    Task ApplyEventsAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default);
}
