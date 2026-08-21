namespace BillingService.Data.Entities;

public enum BillingProvider
{
    Mock,
    YooKassa,
    Stripe
}

public enum SubscriptionManagementMode
{
    ProviderManaged,
    LocallyManaged
}

public enum SubscriptionStatus
{
    Incomplete,
    Trialing,
    Active,
    PastDue,
    Canceled,
    Unpaid
}

public enum InvoiceStatus
{
    Draft,
    Open,
    Paid,
    Uncollectible,
    Void
}
