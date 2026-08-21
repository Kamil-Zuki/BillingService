namespace BillingService.Providers;

public interface IPaymentProviderFactory
{
    IPaymentProvider GetProvider(string providerCode);

    IPaymentProvider GetDefaultProvider();
}
