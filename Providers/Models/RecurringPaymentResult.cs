namespace BillingService.Providers.Models;

public record RecurringPaymentResult(
    string ProviderPaymentId,
    string Status,
    DateTime? PaidAt = null);
