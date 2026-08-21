namespace BillingService.Providers.Models;

public record CheckoutRequest(
    Guid UserId,
    string Email,
    string PlanCode,
    Guid CustomerId,
    int Price,
    string Currency,
    string? ReturnUrl);
