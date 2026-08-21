using BillingService.Data;
using BillingService.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BillingService.Services;

public class EntitlementService : IEntitlementService
{
    private readonly BillingServiceContext _context;
    private readonly BillingOptions _options;

    public EntitlementService(BillingServiceContext context, IOptions<BillingOptions> options)
    {
        _context = context;
        _options = options.Value;
    }

    public async Task<EntitlementResult> GetEntitlementsAsync(Guid userId, CancellationToken cancellationToken = default)
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

        var plan = activeSubscription?.Plan
            ?? await _context.Plans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.IsDefault, cancellationToken)
            ?? await _context.Plans
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

        var entitlements = plan?.Entitlements != null
            ? new Dictionary<string, string>(plan.Entitlements, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new EntitlementResult(
            plan?.Code ?? "free",
            entitlements);
    }
}
