namespace BillingService.Data.Entities;

public class Invoice
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public BillingSubscription Subscription { get; set; } = null!;

    public BillingProvider Provider { get; set; }

    public string ProviderInvoiceId { get; set; } = string.Empty;

    public int AmountDue { get; set; }

    public int AmountPaid { get; set; }

    public string Currency { get; set; } = "RUB";

    public InvoiceStatus Status { get; set; }

    public string? InvoicePdfUrl { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
