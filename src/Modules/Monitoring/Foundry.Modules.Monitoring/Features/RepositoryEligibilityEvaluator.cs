using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class RepositoryEligibilityEvaluator(
    ILogger<RepositoryEligibilityEvaluator> logger) : IRepositoryEligibilityEvaluator
{
    public async Task EvaluateAndStoreAsync(
        MonitoredRepository repo,
        IIssueProvider provider,
        CancellationToken cancellationToken)
    {
        RepositoryEligibility eligibility;

        try
        {
            Result<BranchProtection> result = await provider.GetBranchProtectionAsync(
                repo.Slug,
                cancellationToken);

            eligibility = EvaluateEligibility(result);
        }
#pragma warning disable CA1031 // Provider calls may fail with any exception type (network, serialization, etc.) — treat all as unreachable
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(
                ex,
                "Failed to fetch branch protection for repository {Slug}; marking as unreachable.",
                repo.Slug);
            eligibility = new RepositoryEligibility.Unreachable();
        }

        repo.SetEligibility(eligibility);
    }

    private static RepositoryEligibility EvaluateEligibility(Result<BranchProtection> result)
    {
        if (result is not Result<BranchProtection>.Success success)
        {
            return new RepositoryEligibility.Unreachable();
        }

        BranchProtection protection = success.Value;
        List<EligibilityViolation> violations = [];

        if (!protection.RejectDirectPushes)
        {
            violations.Add(EligibilityViolation.AllowDirectPushes());
        }

        if (!protection.RejectForcePushes)
        {
            violations.Add(EligibilityViolation.AllowForcePushes());
        }

        if (!protection.RejectDeletion)
        {
            violations.Add(EligibilityViolation.AllowDeletion());
        }

        if (violations.Count > 0)
        {
            return new RepositoryEligibility.Ineligible(violations);
        }

        return new RepositoryEligibility.Eligible();
    }
}
