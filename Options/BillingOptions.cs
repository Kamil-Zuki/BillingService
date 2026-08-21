namespace BillingService.Options;

public class BillingOptions
{
    public const string SectionName = "Billing";

    public string DefaultProvider { get; set; } = "mock";

    public int GracePeriodDays { get; set; } = 3;

    public int RenewalPollIntervalMinutes { get; set; } = 15;

    public string? WebhookApiKey { get; set; }
}
