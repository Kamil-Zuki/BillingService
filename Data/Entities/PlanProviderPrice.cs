namespace BillingService.Data.Entities;

public class PlanProviderPrice
{
    public Guid Id { get; set; }

    public Guid PlanId { get; set; }

    public SaaSPlan Plan { get; set; } = null!;

    public BillingProvider Provider { get; set; }

    public string ProviderProductId { get; set; } = string.Empty;

    public string ProviderPriceId { get; set; } = string.Empty;
}
