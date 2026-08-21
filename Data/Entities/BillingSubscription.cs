namespace BillingService.Data.Entities;

public class BillingSubscription
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public Guid PlanId { get; set; }

    public SaaSPlan Plan { get; set; } = null!;

    public BillingProvider Provider { get; set; }

    public string? ProviderSubscriptionId { get; set; }

    public SubscriptionManagementMode ManagementMode { get; set; }

    public SubscriptionStatus Status { get; set; }

    public DateTime CurrentPeriodStart { get; set; }

    public DateTime CurrentPeriodEnd { get; set; }

    public DateTime? TrialStart { get; set; }

    public DateTime? TrialEnd { get; set; }

    public bool CancelAtPeriodEnd { get; set; }

    public DateTime? CanceledAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<Invoice> Invoices { get; set; } = [];
}
