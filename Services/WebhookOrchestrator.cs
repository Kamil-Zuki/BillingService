using BillingService.Data;
using BillingService.Data.Entities;
using BillingService.Providers.Models;
using Microsoft.EntityFrameworkCore;

namespace BillingService.Services;

public class WebhookOrchestrator : IWebhookOrchestrator
{
    private readonly BillingServiceContext _context;
    private readonly IInvoiceService _invoiceService;
    private readonly ILogger<WebhookOrchestrator> _logger;

    public WebhookOrchestrator(
        BillingServiceContext context,
        IInvoiceService invoiceService,
        ILogger<WebhookOrchestrator> logger)
    {
        _context = context;
        _invoiceService = invoiceService;
        _logger = logger;
    }

    public async Task ApplyEventsAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in events)
        {
            switch (domainEvent)
            {
                case PaymentSucceededEvent paymentSucceeded:
                    await ApplyPaymentSucceededAsync(paymentSucceeded, cancellationToken);
                    break;

                case PaymentFailedEvent paymentFailed:
                    await ApplyPaymentFailedAsync(paymentFailed, cancellationToken);
                    break;

                case SubscriptionUpdatedEvent subscriptionUpdated:
                    await ApplySubscriptionUpdatedAsync(subscriptionUpdated, cancellationToken);
                    break;

                case PaymentMethodSavedEvent paymentMethodSaved:
                    await ApplyPaymentMethodSavedAsync(paymentMethodSaved, cancellationToken);
                    break;
            }
        }
    }

    private async Task ApplyPaymentSucceededAsync(PaymentSucceededEvent evt, CancellationToken cancellationToken)
    {
        var subscription = await FindSubscriptionAsync(evt, cancellationToken);
        if (subscription == null)
        {
            _logger.LogWarning("PaymentSucceeded event received for unknown subscription. CustomerId={CustomerId}", evt.ProviderCustomerId);
            return;
        }

        var now = DateTime.UtcNow;
        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodStart = now;
        subscription.CurrentPeriodEnd = now.AddMonths(1);
        subscription.UpdatedAt = now;

        await _invoiceService.HandlePaymentSucceededAsync(evt, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Subscription {SubscriptionId} activated/renewed via payment {PaymentId}",
            subscription.Id, evt.ProviderPaymentId);
    }

    private async Task ApplyPaymentFailedAsync(PaymentFailedEvent evt, CancellationToken cancellationToken)
    {
        var subscription = await FindSubscriptionAsync(evt, cancellationToken);
        if (subscription == null)
        {
            return;
        }

        subscription.Status = SubscriptionStatus.PastDue;
        subscription.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Subscription {SubscriptionId} marked as past_due after failed payment {PaymentId}",
            subscription.Id, evt.ProviderPaymentId);
    }

    private async Task ApplySubscriptionUpdatedAsync(SubscriptionUpdatedEvent evt, CancellationToken cancellationToken)
    {
        var subscription = await _context.Subscriptions
            .Include(s => s.Customer)
            .FirstOrDefaultAsync(
                s => s.ProviderSubscriptionId == evt.ProviderSubscriptionId || s.CustomerId.ToString() == evt.ProviderCustomerId,
                cancellationToken);

        if (subscription == null)
        {
            return;
        }

        if (Enum.TryParse<SubscriptionStatus>(evt.Status, true, out var status))
        {
            subscription.Status = status;
        }

        if (evt.CurrentPeriodStart.HasValue)
        {
            subscription.CurrentPeriodStart = evt.CurrentPeriodStart.Value;
        }

        if (evt.CurrentPeriodEnd.HasValue)
        {
            subscription.CurrentPeriodEnd = evt.CurrentPeriodEnd.Value;
        }

        if (evt.TrialEnd.HasValue)
        {
            subscription.TrialEnd = evt.TrialEnd.Value;
        }

        subscription.CancelAtPeriodEnd = evt.CancelAtPeriodEnd;
        subscription.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyPaymentMethodSavedAsync(PaymentMethodSavedEvent evt, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(evt.ProviderCustomerId, out var customerId))
        {
            return;
        }

        var existing = await _context.PaymentMethods
            .FirstOrDefaultAsync(
                pm => pm.ProviderPaymentMethodId == evt.ProviderPaymentMethodId && pm.CustomerId == customerId,
                cancellationToken);

        if (existing != null)
        {
            existing.Type = evt.Type;
            existing.Brand = evt.Brand;
            existing.Last4 = evt.Last4;
            existing.ExpMonth = evt.ExpMonth;
            existing.ExpYear = evt.ExpYear;
            existing.IsDefault = true;
        }
        else
        {
            // clear default flag from other methods
            var defaults = await _context.PaymentMethods
                .Where(pm => pm.CustomerId == customerId && pm.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var d in defaults)
            {
                d.IsDefault = false;
            }

            _context.PaymentMethods.Add(new PaymentMethod
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                Provider = BillingProvider.YooKassa, // derive from context if multi-provider later
                ProviderPaymentMethodId = evt.ProviderPaymentMethodId,
                Type = evt.Type,
                Brand = evt.Brand,
                Last4 = evt.Last4,
                ExpMonth = evt.ExpMonth,
                ExpYear = evt.ExpYear,
                IsDefault = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<BillingSubscription?> FindSubscriptionAsync(PaymentEventBase evt, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(evt.ProviderSubscriptionId))
        {
            return await _context.Subscriptions
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(
                    s => s.ProviderSubscriptionId == evt.ProviderSubscriptionId,
                    cancellationToken);
        }

        if (Guid.TryParse(evt.ProviderCustomerId, out var customerId))
        {
            return await _context.Subscriptions
                .Include(s => s.Customer)
                .Where(s => s.CustomerId == customerId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }
}
