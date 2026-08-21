using System.Text.Json;
using BillingService.Providers.Models;

namespace BillingService.Providers;

public class MockPaymentProvider : IPaymentProvider
{
    private readonly ILogger<MockPaymentProvider> _logger;
    private readonly Dictionary<string, PaymentStatusResult> _paymentStatuses = new();

    public string ProviderCode => "mock";

    public MockPaymentProvider(ILogger<MockPaymentProvider> logger)
    {
        _logger = logger;
    }

    public Task<CheckoutSessionResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        var paymentId = $"mock_payment_{Guid.NewGuid():N}";
        _paymentStatuses[paymentId] = new PaymentStatusResult("succeeded");

        _logger.LogInformation("Mock checkout created for user {UserId} plan {PlanCode}: {PaymentId}",
            request.UserId, request.PlanCode, paymentId);

        var baseUrl = !string.IsNullOrWhiteSpace(request.ReturnUrl)
            ? request.ReturnUrl
            : "http://localhost:3000/billing/success";

        var delimiter = baseUrl.Contains('?') ? "&" : "?";
        var redirectUrl = $"{baseUrl}{delimiter}provider=mock&paymentId={paymentId}";

        return Task.FromResult(new CheckoutSessionResult(
            redirectUrl,
            paymentId,
            paymentId));
    }

    public Task<RecurringPaymentResult> CreateRecurringPaymentAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var paymentId = $"mock_recurring_{Guid.NewGuid():N}";
        var paidAt = DateTime.UtcNow;
        _paymentStatuses[paymentId] = new PaymentStatusResult("succeeded", paidAt);

        _logger.LogInformation("Mock recurring payment created for subscription {SubscriptionId}: {PaymentId}",
            request.SubscriptionId, paymentId);

        return Task.FromResult(new RecurringPaymentResult(paymentId, "succeeded", paidAt));
    }

    public Task<PaymentStatusResult> GetPaymentStatusAsync(string providerPaymentId, CancellationToken cancellationToken = default)
    {
        if (_paymentStatuses.TryGetValue(providerPaymentId, out var status))
        {
            return Task.FromResult(status);
        }

        return Task.FromResult(new PaymentStatusResult("unknown"));
    }

    public Task<WebhookHandleResult> HandleWebhookAsync(WebhookPayload payload, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Mock webhook received: {Body}", payload.Body);
        try
        {
            var doc = JsonDocument.Parse(payload.Body);
            if (doc.RootElement.TryGetProperty("status", out var statusEl) && statusEl.GetString() == "succeeded")
            {
                var paymentId = doc.RootElement.TryGetProperty("paymentId", out var pidEl) ? pidEl.GetString() : null;
                if (!string.IsNullOrEmpty(paymentId))
                {
                    var evt = new PaymentSucceededEvent(
                        paymentId, // ProviderPaymentId
                        paymentId, // ProviderSubscriptionId
                        "mock_customer", // ProviderCustomerId
                        "mock_pm", // ProviderPaymentMethodId
                        null, // ProviderInvoiceId
                        0, // Amount
                        "RUB", // Currency
                        DateTime.UtcNow); // PaidAt
                    
                    return Task.FromResult(new WebhookHandleResult(new[] { evt }));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse mock webhook payload");
        }
        return Task.FromResult(WebhookHandleResult.Empty);
    }

    public bool VerifyWebhookSignature(WebhookPayload payload, string? secret)
    {
        return true;
    }

    public void SetPaymentStatus(string providerPaymentId, PaymentStatusResult status)
    {
        _paymentStatuses[providerPaymentId] = status;
    }
}
