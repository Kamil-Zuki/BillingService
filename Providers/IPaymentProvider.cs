using BillingService.Providers.Models;

namespace BillingService.Providers;

public interface IPaymentProvider
{
    string ProviderCode { get; }

    Task<CheckoutSessionResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default);

    Task<RecurringPaymentResult> CreateRecurringPaymentAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default);

    Task<PaymentStatusResult> GetPaymentStatusAsync(string providerPaymentId, CancellationToken cancellationToken = default);

    Task<WebhookHandleResult> HandleWebhookAsync(WebhookPayload payload, CancellationToken cancellationToken = default);

    bool VerifyWebhookSignature(WebhookPayload payload, string? secret);
}
