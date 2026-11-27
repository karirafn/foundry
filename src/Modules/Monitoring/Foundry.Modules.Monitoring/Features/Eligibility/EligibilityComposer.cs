using System.Diagnostics;

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
            WriteProbeVerdict.Unknown unknown => FromUnknownReason(unknown.Reason),
            WriteProbeVerdict.Granted => await EvaluateBranchRulesAsync(
                slug,
                credential,
                token,
                cancellationToken),
            _ => throw new UnreachableException($"Unhandled WriteProbeVerdict variant: {verdict.GetType().Name}"),
        };
    }

    // One arm per reason, so a reason added later fails loudly instead of showing as NeverProbed.
    private static RepositoryEligibility.Unreachable FromUnknownReason(UnknownReason reason) =>
        reason switch
        {
            UnknownReason.Transport => new RepositoryEligibility.Unreachable(UnreachableReason.NeverProbed),
            UnknownReason.RateLimited => new RepositoryEligibility.Unreachable(UnreachableReason.RateLimited),
            UnknownReason.AccessRefused => new RepositoryEligibility.Unreachable(UnreachableReason.AccessRefused),
            _ => throw new UnreachableException($"Unhandled UnknownReason: {reason}"),
        };

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
            return new RepositoryEligibility.Unreachable(UnreachableReason.BranchRulesUnavailable);
        }

        IReadOnlyList<EligibilityViolation> violations = success.Value.Violations;

        if (violations.Count > 0)
        {
            return new RepositoryEligibility.Ineligible(violations);
        }

        return new RepositoryEligibility.Eligible();
    }
}
