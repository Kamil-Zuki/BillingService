namespace BillingService.Data.Entities;

public class PaymentMethod
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public BillingProvider Provider { get; set; }

    public string ProviderPaymentMethodId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Brand { get; set; }

    public string? Last4 { get; set; }

    public int? ExpMonth { get; set; }

    public int? ExpYear { get; set; }

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }
}
