using BillingService.Data;
using BillingService.Data.Entities;
using BillingService.Options;
using BillingService.Providers;
using BillingService.Providers.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BillingService.Services;

public class RenewalWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly BillingOptions _options;
    private readonly ILogger<RenewalWorker> _logger;

    public RenewalWorker(
        IServiceProvider serviceProvider,
        IOptions<BillingOptions> options,
        ILogger<RenewalWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Renewal worker started with poll interval {Interval} minutes", _options.RenewalPollIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRenewalsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during renewal processing");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.RenewalPollIntervalMinutes), stoppingToken);
        }
    }

    private async Task ProcessRenewalsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingServiceContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IPaymentProviderFactory>();

        var now = DateTime.UtcNow;
        var graceCutoff = now.AddDays(-_options.GracePeriodDays);
        var renewalWindow = now.AddHours(1);

        // Подписки PastDue после grace period переводим в canceled
        var expiredGrace = await context.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.PastDue)
            .Where(s => s.CurrentPeriodEnd < graceCutoff)
            .ToListAsync(cancellationToken);

        foreach (var subscription in expiredGrace)
        {
            subscription.Status = SubscriptionStatus.Canceled;
            subscription.CanceledAt ??= now;
            subscription.UpdatedAt = now;
            _logger.LogWarning(
                "Subscription {SubscriptionId} canceled after grace period ({GraceDays} days)",
                subscription.Id,
                _options.GracePeriodDays);
        }

        if (expiredGrace.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        var subscriptions = await context.Subscriptions
            .Include(s => s.Customer)
            .Include(s => s.Plan)
            .Where(s => s.ManagementMode == SubscriptionManagementMode.LocallyManaged)
            .Where(s =>
                (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing)
                    && s.CurrentPeriodEnd <= renewalWindow
                || s.Status == SubscriptionStatus.PastDue
                    && s.CurrentPeriodEnd >= graceCutoff)
            .Where(s => !s.CancelAtPeriodEnd)
            .ToListAsync(cancellationToken);

        foreach (var subscription in subscriptions)
        {
            try
            {
                var provider = factory.GetProvider(subscription.Provider.ToString().ToLowerInvariant());

                var defaultPaymentMethod = await context.PaymentMethods
                    .Where(pm => pm.CustomerId == subscription.CustomerId && pm.IsDefault)
                    .OrderByDescending(pm => pm.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (defaultPaymentMethod == null)
                {
                    _logger.LogWarning("No default payment method for subscription {SubscriptionId}", subscription.Id);
                    continue;
                }

                var result = await provider.CreateRecurringPaymentAsync(
                    new RecurringPaymentRequest(
                        subscription.CustomerId,
                        subscription.Customer.ProviderCustomerId ?? subscription.CustomerId.ToString("N"),
                        defaultPaymentMethod.ProviderPaymentMethodId,
                        subscription.Id,
                        subscription.Plan.Code,
                        subscription.Plan.Price,
                        subscription.Plan.Currency),
                    cancellationToken);

                if (result.Status.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    subscription.Status = SubscriptionStatus.Active;
                    subscription.CurrentPeriodStart = DateTime.UtcNow;
                    subscription.CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1);
                    subscription.TrialStart = null;
                    subscription.TrialEnd = null;
                }
                else
                {
                    subscription.Status = SubscriptionStatus.PastDue;
                }

                subscription.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Renewed subscription {SubscriptionId} with payment {PaymentId}, status {Status}",
                    subscription.Id, result.ProviderPaymentId, result.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to renew subscription {SubscriptionId}", subscription.Id);
            }
        }
    }
}
