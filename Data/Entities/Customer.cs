namespace BillingService.Data.Entities;

public class Customer
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public BillingProvider Provider { get; set; }

    public string? ProviderCustomerId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public List<BillingSubscription> Subscriptions { get; set; } = [];

    public List<PaymentMethod> PaymentMethods { get; set; } = [];
}
