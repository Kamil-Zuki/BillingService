namespace BillingService.Providers.Models;

public record CheckoutSessionResult(
    string ConfirmationUrl,
    string ProviderPaymentId,
    string? ProviderSubscriptionId = null);
