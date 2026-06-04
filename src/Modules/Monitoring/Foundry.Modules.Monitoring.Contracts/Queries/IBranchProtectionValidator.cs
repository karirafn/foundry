using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Queries;

public interface IBranchProtectionValidator
{
    Task<Result<IReadOnlyList<EligibilityViolationInfo>>> ValidateAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);
}
