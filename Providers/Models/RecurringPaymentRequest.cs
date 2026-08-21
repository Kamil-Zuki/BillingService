namespace BillingService.Providers.Models;

public record RecurringPaymentRequest(
    Guid CustomerId,
    string ProviderCustomerId,
    string ProviderPaymentMethodId,
    Guid SubscriptionId,
    string PlanCode,
    int Amount,
    string Currency);
