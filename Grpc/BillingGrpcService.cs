using System.Security.Cryptography;
using System.Text;
using BillingService.Data;
using BillingService.Data.Entities;
using BillingService.Providers;
using BillingService.Services;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Pvs.Billing.Grpc;
using Google.Protobuf.WellKnownTypes;
using DataInvoice = BillingService.Data.Entities.Invoice;
using static Pvs.Billing.Grpc.BillingService;

namespace BillingService.Grpc;

public class BillingGrpcService : BillingServiceBase
{
    private readonly IAccessService _accessService;
    private readonly IEntitlementService _entitlementService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IInvoiceService _invoiceService;
    private readonly IWebhookOrchestrator _webhookOrchestrator;
    private readonly IPaymentProviderFactory _providerFactory;
    private readonly BillingServiceContext _context;
    private readonly ILogger<BillingGrpcService> _logger;

    public BillingGrpcService(
        IAccessService accessService,
        IEntitlementService entitlementService,
        ISubscriptionService subscriptionService,
        IInvoiceService invoiceService,
        IWebhookOrchestrator webhookOrchestrator,
        IPaymentProviderFactory providerFactory,
        BillingServiceContext context,
        ILogger<BillingGrpcService> logger)
    {
        _accessService = accessService;
        _entitlementService = entitlementService;
        _subscriptionService = subscriptionService;
        _invoiceService = invoiceService;
        _webhookOrchestrator = webhookOrchestrator;
        _providerFactory = providerFactory;
        _context = context;
        _logger = logger;
    }

    public override async Task<CheckAccessResponse> CheckAccess(CheckAccessRequest request, ServerCallContext context)
    {
        var userId = ParseUserId(request.UserId, context);
        var result = await _accessService.CheckAccessAsync(userId, context.CancellationToken);

        var response = new CheckAccessResponse
        {
            HasAccess = result.HasAccess,
            PlanCode = result.PlanCode,
            Status = result.Status
        };

        if (result.CurrentPeriodEnd.HasValue)
        {
            response.CurrentPeriodEnd = Timestamp.FromDateTime(result.CurrentPeriodEnd.Value.ToUniversalTime());
        }

        return response;
    }

    public override async Task<GetEntitlementsResponse> GetEntitlements(GetEntitlementsRequest request, ServerCallContext context)
    {
        var userId = ParseUserId(request.UserId, context);
        var result = await _entitlementService.GetEntitlementsAsync(userId, context.CancellationToken);

        var response = new GetEntitlementsResponse
        {
            PlanCode = result.PlanCode
        };

        foreach (var entitlement in result.Entitlements)
        {
            response.Entitlements.Add(entitlement.Key, entitlement.Value);
        }

        return response;
    }

    public override async Task<GetSubscriptionResponse> GetSubscription(GetSubscriptionRequest request, ServerCallContext context)
    {
        var userId = ParseUserId(request.UserId, context);
        var subscription = await _subscriptionService.GetActiveSubscriptionAsync(userId, context.CancellationToken);

        return new GetSubscriptionResponse
        {
            Subscription = subscription != null ? MapSubscription(subscription) : null
        };
    }

    public override async Task<ListPlansResponse> ListPlans(ListPlansRequest request, ServerCallContext context)
    {
        var query = _context.Plans.AsNoTracking();
        if (request.OnlyActive)
        {
            query = query.Where(p => p.IsActive);
        }

        var plans = await query.OrderBy(p => p.Price).ToListAsync(context.CancellationToken);
        var response = new ListPlansResponse();
        response.Plans.AddRange(plans.Select(MapPlan));
        return response;
    }

    public override async Task<CreateCheckoutResponse> CreateCheckout(CreateCheckoutRequest request, ServerCallContext context)
    {
        var userId = ParseUserId(request.UserId, context);
        var (subscription, checkout) = await _subscriptionService.CreateCheckoutAsync(
            userId,
            request.Email,
            request.PlanCode,
            string.IsNullOrWhiteSpace(request.Provider) ? null : request.Provider,
            string.IsNullOrWhiteSpace(request.ReturnUrl) ? null : request.ReturnUrl,
            context.CancellationToken);

        return new CreateCheckoutResponse
        {
            CheckoutUrl = checkout.ConfirmationUrl,
            ProviderPaymentId = checkout.ProviderPaymentId
        };
    }

    public override async Task<CancelSubscriptionResponse> CancelSubscription(CancelSubscriptionRequest request, ServerCallContext context)
    {
        var userId = ParseUserId(request.UserId, context);
        var subscription = await _subscriptionService.CancelSubscriptionAsync(
            userId,
            request.CancelAtPeriodEnd,
            context.CancellationToken);

        return new CancelSubscriptionResponse
        {
            Subscription = subscription != null ? MapSubscription(subscription) : null
        };
    }

    public override async Task<ListInvoicesResponse> ListInvoices(ListInvoicesRequest request, ServerCallContext context)
    {
        var userId = ParseUserId(request.UserId, context);
        var invoices = await _invoiceService.ListInvoicesAsync(
            userId,
            request.Page,
            request.PageSize,
            context.CancellationToken);

        var response = new ListInvoicesResponse();
        response.Invoices.AddRange(invoices.Select(MapInvoice));
        return response;
    }

    public override async Task<EnsureCustomerResponse> EnsureCustomer(EnsureCustomerRequest request, ServerCallContext context)
    {
        var userId = ParseUserId(request.UserId, context);
        var customer = await _subscriptionService.EnsureCustomerAsync(userId, request.Email, context.CancellationToken);

        return new EnsureCustomerResponse
        {
            CustomerId = customer.Id.ToString(),
            Provider = customer.Provider.ToString().ToLowerInvariant()
        };
    }

    public override async Task<ProcessWebhookResponse> ProcessWebhook(ProcessWebhookRequest request, ServerCallContext context)
    {
        var providerCode = request.Provider;
        var paymentProvider = _providerFactory.GetProvider(providerCode);

        var eventId = ComputePayloadHash(request.Payload);
        var existing = await _context.ProcessedWebhooks
            .FirstOrDefaultAsync(
                pw => pw.Provider == ParseProvider(providerCode) && pw.EventId == eventId,
                context.CancellationToken);

        if (existing != null)
        {
            return new ProcessWebhookResponse { Processed = true };
        }

        var payload = new Providers.Models.WebhookPayload(request.Payload, request.Signature, eventId);

        var events = await paymentProvider.HandleWebhookAsync(payload, context.CancellationToken);
        await _webhookOrchestrator.ApplyEventsAsync(events.Events, context.CancellationToken);

        _context.ProcessedWebhooks.Add(new ProcessedWebhook
        {
            Provider = ParseProvider(providerCode),
            EventId = eventId,
            EventType = events.Events.FirstOrDefault()?.GetType().Name ?? "unknown",
            ProcessedAt = DateTime.UtcNow,
            PayloadHash = eventId
        });

        await _context.SaveChangesAsync(context.CancellationToken);

        return new ProcessWebhookResponse { Processed = true };
    }

    private static string ComputePayloadHash(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    private static BillingProvider ParseProvider(string providerCode)
    {
        return providerCode.ToLowerInvariant() switch
        {
            "yookassa" => BillingProvider.YooKassa,
            "stripe" => BillingProvider.Stripe,
            _ => BillingProvider.Mock
        };
    }

    private static Guid ParseUserId(string userId, ServerCallContext context)
    {
        if (!Guid.TryParse(userId, out var parsed))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user_id format."));
        }

        return parsed;
    }

    public override async Task<GetUsersBillingStateResponse> GetUsersBillingState(GetUsersBillingStateRequest request, ServerCallContext context)
    {
        var response = new GetUsersBillingStateResponse();
        
        // We fetch active subscriptions for the requested user IDs, or all active subscriptions if no IDs provided
        var query = _context.Subscriptions
            .Include(s => s.Plan)
            .Include(s => s.Customer)
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing);

        if (request.UserIds.Count > 0)
        {
            var ids = request.UserIds.Select(id => Guid.Parse(id)).ToList();
            query = query.Where(s => ids.Contains(s.Customer.UserId));
        }

        var activeSubs = await query.ToListAsync();
        var subDict = activeSubs.ToDictionary(s => s.Customer.UserId.ToString(), s => s.Plan.Code);

        // For all requested users, if they don't have an active sub, they are on 'free' plan
        var defaultPlan = await _context.Plans.FirstOrDefaultAsync(p => p.IsDefault) 
                          ?? new SaaSPlan { Code = "free" };

        if (request.UserIds.Count > 0)
        {
            foreach (var uid in request.UserIds)
            {
                response.States.Add(uid, new UserBillingState
                {
                    PlanCode = subDict.GetValueOrDefault(uid, defaultPlan.Code)
                });
            }
        }
        else
        {
            foreach (var kvp in subDict)
            {
                response.States.Add(kvp.Key, new UserBillingState { PlanCode = kvp.Value });
            }
        }

        return response;
    }

    public override async Task<UpdatePlanEntitlementsResponse> UpdatePlanEntitlements(UpdatePlanEntitlementsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.PlanId, out var planId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid plan ID"));

        var plan = await _context.Plans.FindAsync(planId)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Plan not found"));

        plan.Entitlements = request.Entitlements.ToDictionary(k => k.Key, v => v.Value);
        await _context.SaveChangesAsync();

        return new UpdatePlanEntitlementsResponse
        {
            Plan = MapPlan(plan)
        };
    }

    private static Pvs.Billing.Grpc.Plan MapPlan(SaaSPlan plan)
    {
        var dto = new Pvs.Billing.Grpc.Plan
        {
            Id = plan.Id.ToString(),
            Code = plan.Code,
            Name = plan.Name,
            Description = plan.Description,
            Price = plan.Price,
            Currency = plan.Currency,
            Interval = plan.Interval,
            IsActive = plan.IsActive,
            IsDefault = plan.IsDefault,
            TrialDays = plan.TrialDays
        };

        foreach (var entitlement in plan.Entitlements)
        {
            dto.Entitlements.Add(entitlement.Key, entitlement.Value);
        }

        return dto;
    }

    private static Subscription MapSubscription(BillingSubscription subscription)
    {
        var dto = new Subscription
        {
            Id = subscription.Id.ToString(),
            PlanCode = subscription.Plan.Code,
            Provider = subscription.Provider.ToString().ToLowerInvariant(),
            Status = subscription.Status.ToString().ToLowerInvariant(),
            CurrentPeriodStart = Timestamp.FromDateTime(subscription.CurrentPeriodStart.ToUniversalTime()),
            CurrentPeriodEnd = Timestamp.FromDateTime(subscription.CurrentPeriodEnd.ToUniversalTime()),
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            CreatedAt = Timestamp.FromDateTime(subscription.CreatedAt.ToUniversalTime())
        };

        if (subscription.TrialStart.HasValue)
        {
            dto.TrialStart = Timestamp.FromDateTime(subscription.TrialStart.Value.ToUniversalTime());
        }

        if (subscription.TrialEnd.HasValue)
        {
            dto.TrialEnd = Timestamp.FromDateTime(subscription.TrialEnd.Value.ToUniversalTime());
        }

        if (subscription.CanceledAt.HasValue)
        {
            dto.CanceledAt = Timestamp.FromDateTime(subscription.CanceledAt.Value.ToUniversalTime());
        }

        return dto;
    }

    private static Pvs.Billing.Grpc.Invoice MapInvoice(DataInvoice invoice)
    {
        var dto = new Pvs.Billing.Grpc.Invoice
        {
            Id = invoice.Id.ToString(),
            SubscriptionId = invoice.SubscriptionId.ToString(),
            Provider = invoice.Provider.ToString().ToLowerInvariant(),
            ProviderInvoiceId = invoice.ProviderInvoiceId,
            AmountDue = invoice.AmountDue,
            AmountPaid = invoice.AmountPaid,
            Currency = invoice.Currency,
            Status = invoice.Status.ToString().ToLowerInvariant(),
            CreatedAt = Timestamp.FromDateTime(invoice.CreatedAt.ToUniversalTime())
        };

        if (!string.IsNullOrWhiteSpace(invoice.InvoicePdfUrl))
        {
            dto.InvoicePdfUrl = invoice.InvoicePdfUrl;
        }

        if (invoice.PaidAt.HasValue)
        {
            dto.PaidAt = Timestamp.FromDateTime(invoice.PaidAt.Value.ToUniversalTime());
        }

        return dto;
    }

    public override async Task<AdminAssignPlanResponse> AdminAssignPlan(AdminAssignPlanRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user ID"));

        var plan = await _context.Plans.FirstOrDefaultAsync(p => p.Code == request.PlanCode)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Plan '{request.PlanCode}' not found"));

        // Find or create a customer record for this user
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserId == userId);
        if (customer == null)
        {
            customer = new Customer
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = BillingProvider.Mock,
                ProviderCustomerId = $"admin_assigned_{userId}",
                CreatedAt = DateTime.UtcNow
            };
            _context.Customers.Add(customer);
        }

        // Find existing active subscription and cancel it
        var existingSub = await _context.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.CustomerId == customer.Id &&
                (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing));

        if (existingSub != null)
        {
            existingSub.Status = SubscriptionStatus.Canceled;
            existingSub.CanceledAt = DateTime.UtcNow;
            existingSub.UpdatedAt = DateTime.UtcNow;
        }

        // Create new subscription on the target plan
        var now = DateTime.UtcNow;
        var newSub = new BillingSubscription
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            PlanId = plan.Id,
            Provider = BillingProvider.Mock,
            ManagementMode = SubscriptionManagementMode.LocallyManaged,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddYears(100), // Admin-assigned: no expiry
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.Subscriptions.Add(newSub);

        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin assigned plan '{PlanCode}' to user {UserId}", request.PlanCode, request.UserId);

        return new AdminAssignPlanResponse { PlanCode = plan.Code };
    }
}
