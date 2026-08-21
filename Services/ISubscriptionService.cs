using BillingService.Data.Entities;
using BillingService.Providers.Models;

namespace BillingService.Services;

public interface ISubscriptionService
{
    Task<BillingSubscription?> GetActiveSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<(BillingSubscription Subscription, CheckoutSessionResult Checkout)> CreateCheckoutAsync(
        Guid userId,
        string email,
        string planCode,
        string? providerOverride,
        string? returnUrl,
        CancellationToken cancellationToken = default);

    Task<BillingSubscription?> CancelSubscriptionAsync(
        Guid userId,
        bool cancelAtPeriodEnd,
        CancellationToken cancellationToken = default);

    Task<Customer> EnsureCustomerAsync(Guid userId, string email, CancellationToken cancellationToken = default);
}
