namespace BillingService.Providers.Models;

public abstract record DomainEvent;

public abstract record PaymentEventBase(
    string ProviderPaymentId,
    string? ProviderSubscriptionId,
    string ProviderCustomerId) : DomainEvent;

public record PaymentSucceededEvent(
    string ProviderPaymentId,
    string? ProviderSubscriptionId,
    string ProviderCustomerId,
    string ProviderPaymentMethodId,
    string? ProviderInvoiceId,
    int Amount,
    string Currency,
    DateTime PaidAt,
    string? PlanCode = null,
    string? CardBrand = null,
    string? CardLast4 = null,
    int? ExpMonth = null,
    int? ExpYear = null)
    : PaymentEventBase(ProviderPaymentId, ProviderSubscriptionId, ProviderCustomerId);

public record PaymentFailedEvent(
    string ProviderPaymentId,
    string? ProviderSubscriptionId,
    string ProviderCustomerId,
    string? ProviderPaymentMethodId,
    DateTime FailedAt,
    string? Reason = null)
    : PaymentEventBase(ProviderPaymentId, ProviderSubscriptionId, ProviderCustomerId);

public record SubscriptionUpdatedEvent(
    string? ProviderSubscriptionId,
    string ProviderCustomerId,
    string Status,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd,
    DateTime? TrialEnd,
    bool CancelAtPeriodEnd) : DomainEvent;

public record PaymentMethodSavedEvent(
    string ProviderPaymentMethodId,
    string ProviderCustomerId,
    string Type,
    string? Brand,
    string? Last4,
    int? ExpMonth,
    int? ExpYear) : DomainEvent;
