using BillingService.Data.Entities;

namespace BillingService.Services;

/// <summary>
/// Общая логика выбора подписки с учётом grace period для PastDue.
/// </summary>
internal static class SubscriptionQueryHelper
{
    public static BillingSubscription? FindEffectiveSubscription(
        IEnumerable<BillingSubscription> subscriptions,
        DateTime now,
        int gracePeriodDays)
    {
        var graceCutoff = now.AddDays(-gracePeriodDays);

        return subscriptions
            .Where(s =>
                (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing)
                    && s.CurrentPeriodEnd > now
                || s.Status == SubscriptionStatus.PastDue
                    && s.CurrentPeriodEnd >= graceCutoff)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefault();
    }
}
