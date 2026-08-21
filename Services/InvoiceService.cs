using BillingService.Data;
using BillingService.Data.Entities;
using BillingService.Providers.Models;
using Microsoft.EntityFrameworkCore;

namespace BillingService.Services;

public class InvoiceService : IInvoiceService
{
    private readonly BillingServiceContext _context;

    public InvoiceService(BillingServiceContext context)
    {
        _context = context;
    }

    public async Task<List<Invoice>> ListInvoicesAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        return await _context.Invoices
            .AsNoTracking()
            .Include(i => i.Subscription)
            .Where(i => i.Subscription.Customer.UserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task HandlePaymentSucceededAsync(PaymentSucceededEvent evt, CancellationToken cancellationToken = default)
    {
        var invoiceId = !string.IsNullOrWhiteSpace(evt.ProviderInvoiceId)
            ? evt.ProviderInvoiceId
            : evt.ProviderPaymentId;

        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            return;
        }

        var subscription = await FindSubscriptionAsync(evt, cancellationToken);
        if (subscription == null)
        {
            return;
        }

        var existing = await _context.Invoices
            .FirstOrDefaultAsync(
                i => i.Provider == subscription.Provider && i.ProviderInvoiceId == invoiceId,
                cancellationToken);

        if (existing != null)
        {
            existing.AmountPaid = evt.Amount;
            existing.Status = InvoiceStatus.Paid;
            existing.PaidAt = evt.PaidAt;
        }
        else
        {
            _context.Invoices.Add(new Invoice
            {
                Id = Guid.NewGuid(),
                SubscriptionId = subscription.Id,
                Provider = subscription.Provider,
                ProviderInvoiceId = invoiceId,
                AmountDue = evt.Amount,
                AmountPaid = evt.Amount,
                Currency = evt.Currency,
                Status = InvoiceStatus.Paid,
                PaidAt = evt.PaidAt,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<BillingSubscription?> FindSubscriptionAsync(PaymentSucceededEvent evt, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(evt.ProviderSubscriptionId))
        {
            return await _context.Subscriptions
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(
                    s => s.ProviderSubscriptionId == evt.ProviderSubscriptionId,
                    cancellationToken);
        }

        if (Guid.TryParse(evt.ProviderCustomerId, out var customerId))
        {
            return await _context.Subscriptions
                .Include(s => s.Customer)
                .Where(s => s.CustomerId == customerId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }
}
