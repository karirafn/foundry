using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class RepositoryEligibilityEvaluator : IRepositoryEligibilityEvaluator
{
    public async Task EvaluateAndStoreAsync(
        MonitoredRepository repo,
        IIssueProvider provider,
        CancellationToken cancellationToken)
    {
        Result<BranchProtection> result = await provider.GetBranchProtectionAsync(
            repo.Slug,
            cancellationToken);

        RepositoryEligibility eligibility = RepositoryEligibility.FromBranchProtection(result);
        repo.SetEligibility(eligibility);
    }
}
