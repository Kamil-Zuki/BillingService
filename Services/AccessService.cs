using BillingService.Data;
using BillingService.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BillingService.Services;

public class AccessService : IAccessService
{
    private readonly BillingServiceContext _context;
    private readonly BillingOptions _options;
    private readonly ILogger<AccessService> _logger;

    public AccessService(
        BillingServiceContext context,
        IOptions<BillingOptions> options,
        ILogger<AccessService> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AccessCheckResult> CheckAccessAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var customer = await _context.Customers
            .AsNoTracking()
            .Include(c => c.Subscriptions)
            .ThenInclude(s => s.Plan)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        var activeSubscription = customer?.Subscriptions is null
            ? null
            : SubscriptionQueryHelper.FindEffectiveSubscription(
                customer.Subscriptions,
                now,
                _options.GracePeriodDays);

        if (activeSubscription != null)
        {
            return new AccessCheckResult(
                true,
                activeSubscription.Plan.Code,
                activeSubscription.Status.ToString().ToLowerInvariant(),
                activeSubscription.CurrentPeriodEnd);
        }

        var defaultPlan = await _context.Plans
            .AsNoTracking()
            .Where(p => p.IsDefault)
            .Select(p => p.Code)
            .FirstOrDefaultAsync(cancellationToken);

        return new AccessCheckResult(
            true,
            defaultPlan ?? "free",
            "active",
            null);
    }
}
