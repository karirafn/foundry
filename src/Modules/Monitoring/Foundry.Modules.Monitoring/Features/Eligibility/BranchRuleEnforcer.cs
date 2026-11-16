using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Eligibility;

/// <summary>
/// Evaluates a newly added repository and, when the operator asked for it, writes Foundry's
/// default-branch rules with the account's branch-rules token (ADR 0084). Runs on operator action
/// only — polling stays read-only toward the provider.
/// </summary>
/// <remarks>
/// The write is attempted only when the full evaluation is <see cref="RepositoryEligibility.Ineligible"/>
/// solely because of <c>branch-protection:*</c> violations, package source violations aside. After a successful write the rules are
/// re-read through <see cref="IRepositoryEligibilityEvaluator.EvaluateBranchRulesAndStoreAsync"/>
/// (no second write probe), and the stored eligibility reflects that re-read — never the write's
/// status code.
/// </remarks>
internal sealed class BranchRuleEnforcer(
    IRepositoryEligibilityEvaluator eligibilityEvaluator,
    IIssueProviderFactory providerFactory,
    ILogger<BranchRuleEnforcer> logger)
{
    private const string BranchProtectionRulePrefix = "branch-protection:";

    internal const string NotEffectiveDetail =
        "The branch rules were written but are not effective — an organization policy or a " +
        "higher-level rule may override them.";

    public async Task<BranchRulesOutcome> EvaluateAndApplyAsync(
        MonitoredRepository repository,
        Credential account,
        bool applyBranchRules,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await eligibilityEvaluator.EvaluateFullyAndStoreAsync(repository, now, cancellationToken);

        string settingsUrl = BranchRulesSettingsUrl.For(account, repository.Slug);

        if (!applyBranchRules)
        {
            return new BranchRulesOutcome(BranchRulesOutcome.NotRequestedKind, SettingsUrl: settingsUrl);
        }

        // Package source violations (ADR 0112) are independent of the branch rules: the rules are judged without them.
        switch (repository.Eligibility)
        {
            case RepositoryEligibility.Eligible:
                return new BranchRulesOutcome(BranchRulesOutcome.AlreadyPresentKind, SettingsUrl: settingsUrl);
            case RepositoryEligibility.Ineligible ineligible when BranchRelated(ineligible.Violations).Count == 0:
                // Only package source violations. With a Granted write probe the branch rules were read and hold;
                // otherwise the package source violations replaced an Unreachable result and the rules are unknown.
                return repository.WriteProbeVerdict is WriteProbeVerdict.Granted
                    ? new BranchRulesOutcome(BranchRulesOutcome.AlreadyPresentKind, SettingsUrl: settingsUrl)
                    : Skipped(BranchRulesOutcome.UnverifiableReason, settingsUrl);
            case RepositoryEligibility.Ineligible ineligible when !AreAllBranchProtection(BranchRelated(ineligible.Violations)):
                return Skipped(BranchRulesOutcome.AccessFailureReason, settingsUrl);
            case RepositoryEligibility.Ineligible:
                break;
            default:
                return Skipped(BranchRulesOutcome.UnverifiableReason, settingsUrl);
        }

        if (account.BranchRulesToken is not ProviderToken.Present rulesToken)
        {
            return Skipped(BranchRulesOutcome.NoRulesTokenReason, settingsUrl);
        }

        Result writeResult = await WriteRulesAsync(repository.Slug, account, rulesToken.Value, cancellationToken);

        if (writeResult is Result.Failure failure)
        {
            string reason = failure.Error.Code == BranchRuleWriteErrors.InsufficientPermissionCode
                ? BranchRulesOutcome.InsufficientPermissionReason
                : BranchRulesOutcome.ProviderErrorReason;

            logger.LogWarning(
                "Applying branch rules to repository {Slug} failed ({ErrorCode}).",
                repository.Slug,
                failure.Error.Code);

            return Failed(reason, failure.Error.Message, settingsUrl);
        }

        // Re-read the rules (no second write probe): the stored eligibility reflects what the
        // provider now enforces, not the write's status code.
        await eligibilityEvaluator.EvaluateBranchRulesAndStoreAsync(repository, cancellationToken);

        if (repository.Eligibility is RepositoryEligibility.Eligible
            || (repository.Eligibility is RepositoryEligibility.Ineligible reread && BranchRelated(reread.Violations).Count == 0))
        {
            return new BranchRulesOutcome(BranchRulesOutcome.AppliedKind);
        }

        logger.LogWarning(
            "Branch rules written to repository {Slug} but the re-read is still {EligibilityStatus}.",
            repository.Slug,
            repository.EligibilityStatus);

        return Failed(BranchRulesOutcome.ProviderErrorReason, NotEffectiveDetail, settingsUrl);
    }

    private async Task<Result> WriteRulesAsync(
        RepositorySlug slug,
        Credential account,
        string rulesToken,
        CancellationToken cancellationToken)
    {
        try
        {
            IIssueProvider provider = providerFactory.CreateProvider(account, rulesToken);
            return await provider.ApplyBranchRulesAsync(slug, cancellationToken);
        }
#pragma warning disable CA1031 // Provider calls may fail with any exception type (network, serialization, etc.) — report as a provider error
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Applying branch rules to repository {Slug} threw.", slug);
            return Result.Fail(new Error(
                "BranchRules.ProviderError",
                "The provider could not be reached while writing the branch rules."));
        }
    }

    private static List<EligibilityViolation> BranchRelated(IReadOnlyList<EligibilityViolation> violations) =>
        violations.Where(v => !EligibilityViolationInfo.IsPackageSourceRule(v.Rule)).ToList();

    private static bool AreAllBranchProtection(IReadOnlyList<EligibilityViolation> violations) =>
        violations.All(v => v.Rule.StartsWith(BranchProtectionRulePrefix, StringComparison.Ordinal));

    private static BranchRulesOutcome Skipped(string reason, string settingsUrl) =>
        new(BranchRulesOutcome.SkippedKind, reason, settingsUrl);

    private static BranchRulesOutcome Failed(string reason, string detail, string settingsUrl) =>
        new(BranchRulesOutcome.FailedKind, reason, settingsUrl, detail);
}
