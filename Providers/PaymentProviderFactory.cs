using BillingService.Options;
using Microsoft.Extensions.Options;

namespace BillingService.Providers;

public class PaymentProviderFactory : IPaymentProviderFactory
{
    private readonly IReadOnlyDictionary<string, IPaymentProvider> _providers;
    private readonly BillingOptions _options;

    public PaymentProviderFactory(IEnumerable<IPaymentProvider> providers, IOptions<BillingOptions> options)
    {
        _providers = providers.ToDictionary(p => p.ProviderCode, StringComparer.OrdinalIgnoreCase);
        _options = options.Value;
    }

    public IPaymentProvider GetProvider(string providerCode)
    {
        if (string.IsNullOrWhiteSpace(providerCode))
        {
            return GetDefaultProvider();
        }

        if (_providers.TryGetValue(providerCode, out var provider))
        {
            return provider;
        }

        throw new NotSupportedException($"Payment provider '{providerCode}' is not supported.");
    }

    public IPaymentProvider GetDefaultProvider()
    {
        if (_providers.TryGetValue(_options.DefaultProvider, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException($"Default payment provider '{_options.DefaultProvider}' is not registered.");
    }
}
