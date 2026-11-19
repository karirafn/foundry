using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class RepositoryMappings
{
    internal static int? ToSeconds(TimeSpan? interval) =>
        interval.HasValue ? (int)interval.Value.TotalSeconds : null;

    internal static RepositorySummary ToSummary(
        MonitoredRepository repo,
        Guid accountId,
        string accountName,
        string providerType,
        int defaultPollIntervalSeconds,
        bool webhookIngressConfigured = false) =>
        new(
            repo.Id.Value,
            repo.Slug.ToString(),
            accountId,
            accountName,
            providerType,
            ToSeconds(repo.PollInterval),
            repo.IsActive,
            repo.Position,
            repo.MaxConcurrentWorkers,
            repo.PollInterval is { } ownInterval ? (int)ownInterval.TotalSeconds : defaultPollIntervalSeconds,
            repo.PollInterval is null,
            repo.LastPolledAt,
            ToEligibilityInfo(repo.Eligibility, repo.WriteProbeVerdict, providerType),
            repo.UntrackSuppressedSince,
            ToWebhookStatusToken(repo.EffectiveWebhookStatus(webhookIngressConfigured)),
            repo.RebaseAfterRun,
            repo.MergeWhenChecksPass);

    internal static string ToWebhookStatusToken(WebhookStatus status) =>
        status switch
        {
            WebhookStatus.NotConfigured => RepositorySummary.WebhookNotConfigured,
            WebhookStatus.NotRegistered => "not-registered",
            WebhookStatus.Pending => "pending",
            WebhookStatus.Active => "active",
            WebhookStatus.Silent => "silent",
            _ => throw new UnreachableException(),
        };

    internal static RepositoryEligibilityInfo? ToEligibilityInfo(
        RepositoryEligibility? eligibility,
        WriteProbeVerdict writeProbeVerdict,
        string providerType) =>
        eligibility switch
        {
            null => null,
            RepositoryEligibility.Eligible => new RepositoryEligibilityInfo("eligible", [], null),
            RepositoryEligibility.Ineligible ineligible => new RepositoryEligibilityInfo(
                "ineligible",
                ineligible.Violations
                    .Select(v => new EligibilityViolationInfo(v.Rule, DeriveDescription(v.Rule, providerType, MissingPermissionLabels(writeProbeVerdict, providerType))))
                    .ToList(),
                null),
            RepositoryEligibility.Unreachable unreachable => new RepositoryEligibilityInfo(
                "unreachable",
                [],
                ToReasonToken(unreachable.Reason)),
            _ => throw new UnreachableException(),
        };

    private static IReadOnlyList<string> MissingPermissionLabels(WriteProbeVerdict verdict, string providerType) =>
        verdict is WriteProbeVerdict.Denied denied
            ? TokenPermissions.LabelsOf(providerType, denied.MissingPermissions)
            : [];

    private static string DeriveDescription(string rule, string providerType, IReadOnlyList<string> missingPermissionLabels)
    {
        if (rule.StartsWith(EligibilityViolationInfo.CannotPushRulePrefix + ":", StringComparison.Ordinal))
        {
            string slug = rule[(EligibilityViolationInfo.CannotPushRulePrefix.Length + 1)..];
            return EligibilityViolationInfo.CannotPushDescription(slug, providerType, missingPermissionLabels);
        }

        if (rule.StartsWith(EligibilityViolationInfo.NoCredentialRulePrefix + ":", StringComparison.Ordinal))
        {
            string namespaceName = rule[(EligibilityViolationInfo.NoCredentialRulePrefix.Length + 1)..];
            return EligibilityViolationInfo.NoCredentialDescription(namespaceName);
        }

        if (rule.StartsWith(EligibilityViolationInfo.PackageSourceUnreachableRulePrefix + ":", StringComparison.Ordinal))
        {
            string url = rule[(EligibilityViolationInfo.PackageSourceUnreachableRulePrefix.Length + 1)..];
            return EligibilityViolationInfo.PackageSourceUnreachableDescription(url);
        }

        return rule switch
        {
            var r when r == EligibilityViolationInfo.AllowDirectPushesRule
                => EligibilityViolationInfo.AllowDirectPushesDescription,
            var r when r == EligibilityViolationInfo.AllowForcePushesRule
                => EligibilityViolationInfo.AllowForcePushesDescription,
            var r when r == EligibilityViolationInfo.AllowDeletionRule
                => EligibilityViolationInfo.AllowDeletionDescription,
            EligibilityViolationInfo.CredentialUnreadableRule
                => EligibilityViolationInfo.CredentialUnreadableDescription,
            EligibilityViolationInfo.CredentialInvalidRule
                => EligibilityViolationInfo.CredentialInvalidDescription,
            EligibilityViolationInfo.NuGetConfigInvalidRule
                => EligibilityViolationInfo.NuGetConfigInvalidDescription,
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
