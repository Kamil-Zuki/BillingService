namespace BillingService.Options;

public class YooKassaOptions
{
    public const string SectionName = "PaymentProviders:YooKassa";

    public string ShopId { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string ReturnUrl { get; set; } = "http://localhost:3000/billing/success";

    public bool UseSandbox { get; set; } = true;
}
