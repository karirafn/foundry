using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class RepositoryMappings
{
    internal static int? ToSeconds(TimeSpan? interval) =>
        interval.HasValue ? (int)interval.Value.TotalSeconds : null;

    internal static RepositoryEligibilityInfo? ToEligibilityInfo(
        RepositoryEligibility? eligibility,
        string providerType) =>
        eligibility switch
        {
            null => null,
            RepositoryEligibility.Eligible => new RepositoryEligibilityInfo("eligible", [], null),
            RepositoryEligibility.Ineligible ineligible => new RepositoryEligibilityInfo(
                "ineligible",
                ineligible.Violations
                    .Select(v => new EligibilityViolationInfo(v.Rule, DeriveDescription(v.Rule, providerType)))
                    .ToList(),
                null),
            RepositoryEligibility.Unreachable unreachable => new RepositoryEligibilityInfo(
                "unreachable",
                [],
                ToReasonToken(unreachable.Reason)),
            _ => throw new UnreachableException(),
        };

    private static string DeriveDescription(string rule, string providerType)
    {
        if (rule.StartsWith(EligibilityViolationInfo.CannotPushRulePrefix + ":", StringComparison.Ordinal))
        {
            string slug = rule[(EligibilityViolationInfo.CannotPushRulePrefix.Length + 1)..];
            return EligibilityViolationInfo.CannotPushDescription(slug, providerType);
        }

        if (rule.StartsWith(EligibilityViolationInfo.NoCredentialRulePrefix + ":", StringComparison.Ordinal))
        {
            string namespaceName = rule[(EligibilityViolationInfo.NoCredentialRulePrefix.Length + 1)..];
            return EligibilityViolationInfo.NoCredentialDescription(namespaceName);
        }

        return rule switch
        {
            var r when r == EligibilityViolationInfo.AllowDirectPushesRule
                => EligibilityViolationInfo.AllowDirectPushesDescription,
            var r when r == EligibilityViolationInfo.AllowForcePushesRule
                => EligibilityViolationInfo.AllowForcePushesDescription,
            var r when r == EligibilityViolationInfo.AllowDeletionRule
                => EligibilityViolationInfo.AllowDeletionDescription,
            _ => "This repository is ineligible for an unknown reason.",
        };
    }

    private static string ToReasonToken(UnreachableReason reason) =>
        reason switch
        {
            UnreachableReason.NeverProbed => RepositoryEligibilityInfo.NeverProbedReason,
            UnreachableReason.RateLimited => RepositoryEligibilityInfo.RateLimitedReason,
            UnreachableReason.BranchRulesUnavailable => RepositoryEligibilityInfo.BranchRulesUnavailableReason,
            _ => throw new UnreachableException(),
        };
}
