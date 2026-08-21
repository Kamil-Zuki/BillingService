using BillingService.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BillingService.Data;

public class BillingServiceContext : DbContext
{
    public BillingServiceContext(DbContextOptions<BillingServiceContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SaaSPlan> Plans => Set<SaaSPlan>();
    public DbSet<PlanProviderPrice> PlanProviderPrices => Set<PlanProviderPrice>();
    public DbSet<BillingSubscription> Subscriptions => Set<BillingSubscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<ProcessedWebhook> ProcessedWebhooks => Set<ProcessedWebhook>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("billing");

        var billingProviderConverter = new EnumToStringConverter<BillingProvider>();
        var managementModeConverter = new EnumToStringConverter<SubscriptionManagementMode>();
        var subscriptionStatusConverter = new EnumToStringConverter<SubscriptionStatus>();
        var invoiceStatusConverter = new EnumToStringConverter<InvoiceStatus>();

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.Property(e => e.Provider).HasConversion(billingProviderConverter);
            entity.HasMany(e => e.Subscriptions).WithOne(s => s.Customer).HasForeignKey(s => s.CustomerId);
            entity.HasMany(e => e.PaymentMethods).WithOne(pm => pm.Customer).HasForeignKey(pm => pm.CustomerId);
        });

        modelBuilder.Entity<SaaSPlan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Entitlements).HasColumnType("jsonb");
            entity.HasMany(e => e.Subscriptions).WithOne(s => s.Plan).HasForeignKey(s => s.PlanId);
            entity.HasMany(e => e.ProviderPrices).WithOne(pp => pp.Plan).HasForeignKey(pp => pp.PlanId);
        });

        modelBuilder.Entity<PlanProviderPrice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PlanId, e.Provider }).IsUnique();
            entity.Property(e => e.Provider).HasConversion(billingProviderConverter);
        });

        modelBuilder.Entity<BillingSubscription>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CustomerId, e.Status });
            entity.Property(e => e.Provider).HasConversion(billingProviderConverter);
            entity.Property(e => e.ManagementMode).HasConversion(managementModeConverter);
            entity.Property(e => e.Status).HasConversion(subscriptionStatusConverter);
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Provider, e.ProviderInvoiceId }).IsUnique();
            entity.Property(e => e.Provider).HasConversion(billingProviderConverter);
            entity.Property(e => e.Status).HasConversion(invoiceStatusConverter);
        });

        modelBuilder.Entity<PaymentMethod>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Provider, e.ProviderPaymentMethodId }).IsUnique();
            entity.Property(e => e.Provider).HasConversion(billingProviderConverter);
        });

        modelBuilder.Entity<ProcessedWebhook>(entity =>
        {
            entity.HasKey(e => new { e.Provider, e.EventId });
            entity.Property(e => e.Provider).HasConversion(billingProviderConverter);
        });

        SeedPlans(modelBuilder);
    }

    private static void SeedPlans(ModelBuilder modelBuilder)
    {
        var freePlanId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var proPlanId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        modelBuilder.Entity<SaaSPlan>().HasData(
            new SaaSPlan
            {
                Id = freePlanId,
                Code = "free",
                Name = "Free",
                Description = "Базовый бесплатный план с ограниченными лимитами.",
                Price = 0,
                Currency = "RUB",
                Interval = "month",
                IsActive = true,
                IsDefault = true,
                TrialDays = 0,
                Entitlements = new Dictionary<string, string>
                {
                    ["maxProjects"] = "3",
                    ["maxCards"] = "500",
                    ["aiRequestsPerDay"] = "10",
                    ["textWorkspaceMaxBooks"] = "3",
                    ["canUseGrammarTutor"] = "false",
                    ["canUseAutoMine"] = "false",
                    ["canUseVoiceAgent"] = "false",
                    ["canUseSpeaking"] = "false"
                }
            },
            new SaaSPlan
            {
                Id = proPlanId,
                Code = "pro",
                Name = "Pro",
                Description = "Полный доступ к платформе с расширенными лимитами.",
                Price = 99000,
                Currency = "RUB",
                Interval = "month",
                IsActive = true,
                IsDefault = false,
                TrialDays = 7,
                Entitlements = new Dictionary<string, string>
                {
                    ["maxProjects"] = "50",
                    ["maxCards"] = "10000",
                    ["aiRequestsPerDay"] = "100",
                    ["textWorkspaceMaxBooks"] = "-1",
                    ["canUseGrammarTutor"] = "true",
                    ["canUseAutoMine"] = "true",
                    ["canUseVoiceAgent"] = "true",
                    ["canUseSpeaking"] = "true"
                }
            });
    }
}
