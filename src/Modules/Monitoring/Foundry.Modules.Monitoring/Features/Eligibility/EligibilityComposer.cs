using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Eligibility;

/// <summary>
/// Composes repository eligibility from a persisted write-probe verdict and a branch-rules GET result.
/// </summary>
/// <remarks>
/// Composition rule:
/// - No credential  → Ineligible([NoCredential])            (handled by caller before reaching here)
/// - Verdict Denied → Ineligible([CannotPush])
/// - Verdict Unknown → Unreachable  (never treat unprobed as eligible — pit-of-success)
/// - Verdict Granted → evaluate branch rules → Eligible / Ineligible(violations) / Unreachable
/// </remarks>
internal sealed class EligibilityComposer(IIssueProviderFactory providerFactory)
{
    public async Task<RepositoryEligibility> ComposeAsync(
        RepositorySlug slug,
        WriteProbeVerdict verdict,
        Credential credential,
        string token,
        CancellationToken cancellationToken)
    {
        return verdict switch
        {
            WriteProbeVerdict.Denied => new RepositoryEligibility.Ineligible(
                [EligibilityViolation.CannotPush(slug)]),
            WriteProbeVerdict.Unknown => new RepositoryEligibility.Unreachable(),
            WriteProbeVerdict.Granted => await EvaluateBranchRulesAsync(
                slug,
                credential,
                token,
                cancellationToken),
            _ => new RepositoryEligibility.Unreachable(),
        };
    }

    private async Task<RepositoryEligibility> EvaluateBranchRulesAsync(
        RepositorySlug slug,
        Credential credential,
        string token,
        CancellationToken cancellationToken)
    {
        IIssueProvider provider = providerFactory.CreateProvider(credential, token);
        Result<BranchProtection> result = await provider.GetBranchProtectionAsync(slug, cancellationToken);
        return EvaluateFromBranchProtection(result);
    }

    internal static RepositoryEligibility EvaluateFromBranchProtection(Result<BranchProtection> result)
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
