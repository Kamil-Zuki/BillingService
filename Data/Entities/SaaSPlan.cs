namespace BillingService.Data.Entities;

public class SaaSPlan
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int Price { get; set; }

    public string Currency { get; set; } = "RUB";

    public string Interval { get; set; } = "month";

    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; }

    public int TrialDays { get; set; }

    public Dictionary<string, string> Entitlements { get; set; } = new();

    public List<BillingSubscription> Subscriptions { get; set; } = [];

    public List<PlanProviderPrice> ProviderPrices { get; set; } = [];
}
