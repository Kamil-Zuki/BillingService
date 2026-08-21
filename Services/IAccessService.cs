namespace BillingService.Services;

public interface IAccessService
{
    Task<AccessCheckResult> CheckAccessAsync(Guid userId, CancellationToken cancellationToken = default);
}

public record AccessCheckResult(
    bool HasAccess,
    string PlanCode,
    string Status,
    DateTime? CurrentPeriodEnd);
