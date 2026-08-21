namespace BillingService.Providers.Models;

public record PaymentStatusResult(
    string Status,
    DateTime? PaidAt = null);
