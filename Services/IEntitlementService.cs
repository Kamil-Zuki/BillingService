namespace BillingService.Services;

public interface IEntitlementService
{
    Task<EntitlementResult> GetEntitlementsAsync(Guid userId, CancellationToken cancellationToken = default);
}

public record EntitlementResult(
    string PlanCode,
    IReadOnlyDictionary<string, string> Entitlements);
