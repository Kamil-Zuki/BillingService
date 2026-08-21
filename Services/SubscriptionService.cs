using BillingService.Data;
using BillingService.Data.Entities;
using BillingService.Options;
using BillingService.Providers;
using BillingService.Providers.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BillingService.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly BillingServiceContext _context;
    private readonly IPaymentProviderFactory _providerFactory;
    private readonly BillingOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        BillingServiceContext context,
        IPaymentProviderFactory providerFactory,
        IOptions<BillingOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _context = context;
        _providerFactory = providerFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BillingSubscription?> GetActiveSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.Subscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => s.Customer.UserId == userId)
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing)
            .Where(s => s.CurrentPeriodEnd > now)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(BillingSubscription Subscription, CheckoutSessionResult Checkout)> CreateCheckoutAsync(
        Guid userId,
        string email,
        string planCode,
        string? providerOverride,
        string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var plan = await _context.Plans
            .FirstOrDefaultAsync(p => p.Code == planCode && p.IsActive, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{planCode}' is not available.");

        var providerCode = string.IsNullOrWhiteSpace(providerOverride)
            ? _options.DefaultProvider
            : providerOverride;

        var provider = _providerFactory.GetProvider(providerCode);

        var customer = await EnsureCustomerAsync(userId, email, cancellationToken);
        customer.Provider = ParseProvider(providerCode);

        var now = DateTime.UtcNow;
        var subscription = new BillingSubscription
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            PlanId = plan.Id,
            Provider = ParseProvider(providerCode),
            ManagementMode = providerCode.Equals("yookassa", StringComparison.OrdinalIgnoreCase)
                ? SubscriptionManagementMode.LocallyManaged
                : SubscriptionManagementMode.ProviderManaged,
            Status = providerCode.Equals("mock", StringComparison.OrdinalIgnoreCase)
                ? (plan.TrialDays > 0 ? SubscriptionStatus.Trialing : SubscriptionStatus.Active)
                : SubscriptionStatus.Incomplete,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = plan.TrialDays > 0
                ? now.AddDays(plan.TrialDays)
                : now.AddMonths(1),
            TrialStart = plan.TrialDays > 0 ? now : null,
            TrialEnd = plan.TrialDays > 0 ? now.AddDays(plan.TrialDays) : null,
            CancelAtPeriodEnd = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Subscriptions.Add(subscription);
        await _context.SaveChangesAsync(cancellationToken);

        var checkoutRequest = new CheckoutRequest(
            userId,
            email,
            planCode,
            customer.Id,
            plan.Price,
            plan.Currency,
            returnUrl);

        var checkout = await provider.CreateCheckoutAsync(checkoutRequest, cancellationToken);
        subscription.ProviderSubscriptionId = checkout.ProviderSubscriptionId;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Checkout created for user {UserId} plan {PlanCode} via {Provider}: {PaymentId}",
            userId, planCode, providerCode, checkout.ProviderPaymentId);

        return (subscription, checkout);
    }

    public async Task<BillingSubscription?> CancelSubscriptionAsync(
        Guid userId,
        bool cancelAtPeriodEnd,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _context.Subscriptions
            .Include(s => s.Customer)
            .Include(s => s.Plan)
            .Where(s => s.Customer.UserId == userId)
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription == null)
        {
            return null;
        }

        subscription.CancelAtPeriodEnd = cancelAtPeriodEnd;
        subscription.CanceledAt = cancelAtPeriodEnd ? DateTime.UtcNow : null;
        subscription.UpdatedAt = DateTime.UtcNow;

        if (!cancelAtPeriodEnd)
        {
            subscription.Status = SubscriptionStatus.Canceled;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Subscription {SubscriptionId} for user {UserId} canceled (atPeriodEnd={CancelAtPeriodEnd})",
            subscription.Id, userId, cancelAtPeriodEnd);

        return subscription;
    }

    public async Task<Customer> EnsureCustomerAsync(Guid userId, string email, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (customer != null)
        {
            if (!string.IsNullOrWhiteSpace(email) && customer.Email != email)
            {
                customer.Email = email;
                await _context.SaveChangesAsync(cancellationToken);
            }

            return customer;
        }

        customer = new Customer
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Email = email,
            Provider = ParseProvider(_options.DefaultProvider),
            CreatedAt = DateTime.UtcNow
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(cancellationToken);

        return customer;
    }

    private static BillingProvider ParseProvider(string providerCode)
    {
        return providerCode.ToLowerInvariant() switch
        {
            "yookassa" => BillingProvider.YooKassa,
            "stripe" => BillingProvider.Stripe,
            _ => BillingProvider.Mock
        };
    }
}
