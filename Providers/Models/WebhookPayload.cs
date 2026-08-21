namespace BillingService.Providers.Models;

public record WebhookPayload(
    string Body,
    string? SignatureHeader,
    string? EventId = null);
