using BillingService.Data.Entities;
using BillingService.Providers.Models;

namespace BillingService.Services;

public interface IInvoiceService
{
    Task<List<Invoice>> ListInvoicesAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task HandlePaymentSucceededAsync(PaymentSucceededEvent evt, CancellationToken cancellationToken = default);
}
