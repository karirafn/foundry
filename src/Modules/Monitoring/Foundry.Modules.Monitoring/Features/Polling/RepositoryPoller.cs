using System.Diagnostics;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Polling;

internal sealed class RepositoryPoller(
    IIssueQueries issueQueries,
    DbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher,
    IIntegrationEventDispatcher integrationEventDispatcher,
    IRepositoryEligibilityEvaluator eligibilityEvaluator,
    WebhookNudgeQueue nudgeQueue,
    ILogger<RepositoryPoller> logger)
{
    /// <summary>
    /// The fixed per-cycle provider call budget that does NOT scale with issue count:
    /// one eligibility branch-rules GET plus one <see cref="IIssueProvider.GetIssuesAsync"/> listing call.
    /// This applies when the repository's write-probe verdict is Granted (the cheap eligibility path),
    /// there are zero review issues, zero dependency candidates, and zero checks-pending issues.
    /// Referenced by ADR 0066 and DOMAIN.md; asserted by the poll-call invariance test.
    /// </summary>
    internal const int MaxFixedPollCallsPerCycle = 2;

    /// <summary>
    /// Grace window for <c>NoneDetected</c> verdicts: the poller waits this long before treating
    /// absent checks as a gate decision. Checks are sometimes registered asynchronously after a
    /// PR opens, so a short grace window avoids premature resolution.
    /// </summary>
    internal static readonly TimeSpan ChecksNoneGraceWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum time the poller will wait for a <c>Pending</c> verdict to resolve.
    /// After this upper bound elapses the issue is advanced to review with outcome <c>Unfinished</c>.
    /// </summary>
    internal static readonly TimeSpan ChecksPendingUpperBound = TimeSpan.FromMinutes(60);

    public async Task<Result> PollAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Result<PollChanges> result = await PollDetectingChangesAsync(repository, provider, now, cancellationToken);

        return result is Result<PollChanges>.Failure failure ? failure.Error : Result.Ok();
    }

    /// <summary>
    /// Runs one full scheduled poll — the composition of every per-pass entry point, in the same order
    /// and with the same saves a nudge would use (ADR 0085). Returns the categories of change found —
    /// candidate <em>missed changes</em>, filtered per provider by the caller: issue detection, detail
    /// change or untrack; a closed issue or pull request; new review feedback; a provider-driven checks
    /// verdict; or a branch-rule change. Time-based checks exits and dependency refreshes never count.
    /// </summary>
    internal async Task<Result<PollChanges>> PollDetectingChangesAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Pass 0: evaluate repository-level eligibility every poll cycle.
        RepositoryEligibility? eligibilityBefore = repository.Eligibility;
        await EvaluateEligibilityAsync(repository, now, cancellationToken);
        bool branchRulesChanged = BranchRuleOutcomeChanged(eligibilityBefore, repository.Eligibility);

        // Passes 1-2: listing (detect, detail change, complete-listing untrack), stamping the poll time.
        Result<bool> listingResult = await RunListingCoreAsync(
            repository,
            provider,
            now,
            markPolled: true,
            cancellationToken);

        if (listingResult is not Result<bool>.Success listingSuccess)
        {
            return listingResult is Result<bool>.Failure listingFailure
                ? Result<PollChanges>.Fail(listingFailure.Error)
                : Result<PollChanges>.Fail(new Error("Monitoring.UnexpectedResult", "The listing pass returned an unexpected result type."));
        }

        // Pass 3: detect dependencies for the dispatch-candidate issues the dependency handler can act on.
        await RunDependencyPassAsync(repository, provider, cancellationToken);

        // Pass 4: check provider-side status of all review issues.
        bool reviewChanged = await RunReviewPassAsync(repository, provider, issueNumber: null, cancellationToken);

        // Pass 5: check provider-side checks verdict for all checks-pending issues.
        bool checksChanged = await RunChecksPassAsync(repository, provider, issueNumber: null, now, cancellationToken);

        PollChanges changes = PollChanges.None;
        changes |= listingSuccess.Value ? PollChanges.IssueListing : PollChanges.None;
        changes |= reviewChanged ? PollChanges.Review : PollChanges.None;
        changes |= checksChanged ? PollChanges.Checks : PollChanges.None;
        changes |= branchRulesChanged ? PollChanges.BranchRules : PollChanges.None;
        return Result<PollChanges>.Ok(changes);
    }

    /// <summary>
    /// Whether the branch-rule outcome changed between two eligibility evaluations. Only outcomes decided
    /// by the default branch's rules compare (<c>Eligible</c>, or <c>Ineligible</c> with branch-rule
    /// violations only); a transition through <c>Unreachable</c>, a write-probe denial or a missing
    /// credential is not a branch-rule change.
    /// </summary>
    internal static bool BranchRuleOutcomeChanged(RepositoryEligibility? before, RepositoryEligibility? after)
    {
        HashSet<string>? beforeRules = BranchRuleViolations(before);
        HashSet<string>? afterRules = BranchRuleViolations(after);

        return beforeRules is not null && afterRules is not null && !beforeRules.SetEquals(afterRules);
    }

    private static HashSet<string>? BranchRuleViolations(RepositoryEligibility? eligibility)
    {
        switch (eligibility)
        {
            case RepositoryEligibility.Eligible:
                return [];
            case RepositoryEligibility.Ineligible ineligible:
                HashSet<string> rules = ineligible.Violations.Select(v => v.Rule).ToHashSet(StringComparer.Ordinal);
                return rules.All(IsBranchRule) ? rules : null;
            default:
                return null;
        }
    }

    private static bool IsBranchRule(string rule) =>
        rule == EligibilityViolation.AllowDirectPushesRule
        || rule == EligibilityViolation.AllowForcePushesRule
        || rule == EligibilityViolation.AllowDeletionRule;

    /// <summary>
    /// The branch-rules eligibility step on its own (nudged by <c>branch_protection_rule</c> and
    /// <c>repository_ruleset</c> deliveries): evaluates and persists the repository's eligibility.
    /// </summary>
    public async Task RunBranchRulesStepAsync(
        MonitoredRepository repository,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EvaluateEligibilityAsync(repository, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The listing pass on its own (nudged by <c>issues</c> deliveries): detection, detail change and the
    /// complete-listing untrack guard. Unlike a scheduled poll it does not stamp <c>LastPolledAt</c>, so
    /// the scheduled cadence is unaffected. Returns whether a webhook-mapped change was found.
    /// </summary>
    public Task<Result<bool>> RunListingPassAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => RunListingCoreAsync(repository, provider, now, markPolled: false, cancellationToken);

    /// <summary>
    /// The dependency pass on its own: refreshes the blockers of every dispatch-candidate issue. Run after
    /// a listing pass on the same provider instance, the GitHub provider answers from the listing's cached
    /// dependency data without further calls.
    /// </summary>
    public async Task RunDependencyPassAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        CancellationToken cancellationToken)
    {
        // Re-query after the listing save so issues newly detected in this cycle are included.
        IReadOnlySet<int> candidateNumbers = await issueQueries.GetDispatchCandidateIssueNumbersAsync(
            repository.Id,
            cancellationToken);

        await DetectDependenciesAsync(repository, provider, candidateNumbers, cancellationToken);

        await integrationEventDispatcher.DispatchAsync(repository.IntegrationEvents, cancellationToken);
        repository.ClearIntegrationEvents();
        await dbContext.SaveChangesAsync(cancellationToken);
        await domainEventDispatcher.DispatchAsync(repository.DomainEvents, cancellationToken);
        repository.ClearDomainEvents();
    }

    /// <summary>
    /// The review pass — for every review issue, or only <paramref name="issueNumber"/> when supplied
    /// (nudged by pull request, review and PR comment deliveries). Returns whether a webhook-mapped
    /// change was found.
    /// </summary>
    public async Task<bool> RunReviewPassAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        int? issueNumber,
        CancellationToken cancellationToken)
    {
        bool changed = await DetectReviewStatusChangesAsync(repository, provider, issueNumber, cancellationToken);

        await integrationEventDispatcher.DispatchAsync(repository.IntegrationEvents, cancellationToken);
        repository.ClearIntegrationEvents();
        await dbContext.SaveChangesAsync(cancellationToken);

        return changed;
    }

    /// <summary>
    /// The checks pass — for every checks-pending issue, or only <paramref name="issueNumber"/> when
    /// supplied (nudged by <c>check_suite</c>, <c>status</c> and deadline nudges). Returns whether a
    /// webhook-mapped change was found.
    /// </summary>
    public async Task<bool> RunChecksPassAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        int? issueNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool changed = await DetectChecksPendingStatusChangesAsync(
            repository,
            provider,
            issueNumber,
            now,
            cancellationToken);

        await integrationEventDispatcher.DispatchAsync(repository.IntegrationEvents, cancellationToken);
        repository.ClearIntegrationEvents();
        await dbContext.SaveChangesAsync(cancellationToken);

        return changed;
    }

    private async Task EvaluateEligibilityAsync(
        MonitoredRepository repository,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Branch on whether a fresh write probe is due (verdict is Unknown and either never attempted
        // or the 15-minute cooldown has elapsed). Repositories with no credential early-return
        // Ineligible(NoCredential) inside both evaluation paths before any probe is issued, so a
        // credential-less repo whose verdict is Unknown stays permanently due at zero API cost.
        if (repository.IsDueForWriteProbe(MonitoredRepository.WriteProbeCooldown, now))
        {
            await eligibilityEvaluator.EvaluateFullyAndStoreAsync(repository, now, cancellationToken);
        }
        else
        {
            // Verdict is Granted, Denied, or Unknown within the cooldown — use the cheap path.
            await eligibilityEvaluator.EvaluateBranchRulesAndStoreAsync(repository, cancellationToken);
        }
    }

    private async Task<Result<bool>> RunListingCoreAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        DateTimeOffset now,
        bool markPolled,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<int> knownNumbers = await issueQueries.GetKnownIssueNumbersAsync(
            repository.Id,
            cancellationToken);

        Result<IssueListing> providerResult = await provider.GetIssuesAsync(
            repository.Slug,
            cancellationToken);

        if (providerResult is not Result<IssueListing>.Success providerSuccess)
        {
            if (providerResult is Result<IssueListing>.Failure f)
            {
                return Result<bool>.Fail(f.Error);
            }

            return Result<bool>.Fail(new Error("Monitoring.UnexpectedResult", "GetIssues returned an unexpected result type."));
        }

        IssueListing listing = providerSuccess.Value;
        IReadOnlyList<ProviderIssue> fetchedIssues = listing.Issues;

        HashSet<int> fetchedNumbers = fetchedIssues
            .Select(i => i.Number)
            .ToHashSet();

        bool detected = DetectNewIssues(repository, fetchedIssues, knownNumbers, now);
        bool detailsChanged = await DetectDetailChangesAsync(repository, fetchedIssues, knownNumbers, cancellationToken);
        bool untracked = false;

        // Only run the untrack pass when the listing is provably complete. An incomplete listing
        // (IsComplete: false, pagination cap reached) cannot distinguish a missing issue from one
        // that simply fell outside the fetched window, so skipping keeps the poll returning success
        // while detection, detail-change, dependency, and review passes all continue normally.
        if (listing.IsComplete)
        {
            // A previously-suppressed repo has recovered — clear suppression before the untrack pass.
            repository.ClearUntrackSuppression();

            IReadOnlySet<int> untrackableNumbers = await issueQueries.GetUntrackableIssueNumbersAsync(
                repository.Id,
                cancellationToken);

            untracked = DetectUntrackedIssues(repository, untrackableNumbers, fetchedNumbers);
        }
        else
        {
            // Log a warning only on the first transition into suppression (null→set).
            // Subsequent incomplete polls are steady-state suppressed and log nothing (AC4).
            bool justSuppressed = repository.SuppressUntracking(now);
            if (justSuppressed)
            {
                logger.LogWarning(
                    "Untrack pass suppressed for repository {Slug}: listing is incomplete. " +
                    "Suppressed since {SuppressedAt}.",
                    repository.Slug,
                    now);
            }
        }

        if (markPolled)
        {
            repository.MarkPolled(now);
        }

        await integrationEventDispatcher.DispatchAsync(repository.IntegrationEvents, cancellationToken);
        repository.ClearIntegrationEvents();
        await dbContext.SaveChangesAsync(cancellationToken);
        await domainEventDispatcher.DispatchAsync(repository.DomainEvents, cancellationToken);
        repository.ClearDomainEvents();

        return Result<bool>.Ok(detected || detailsChanged || untracked);
    }

    private static async Task DetectDependenciesAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        IReadOnlySet<int> issueNumbers,
        CancellationToken cancellationToken)
    {
        // HTTP calls are sequential: GitHub has no batch dependencies endpoint,
        // and issue counts per repository are expected to be small (design decision D6).
        foreach (int issueNumber in issueNumbers)
        {
            Result<IReadOnlyList<int>> dependencyResult = await provider.GetDependenciesAsync(
                repository.Slug,
                issueNumber,
                cancellationToken);

            if (dependencyResult is not Result<IReadOnlyList<int>>.Success dependencySuccess)
            {
                continue;
            }

            repository.RecordIntegrationEvent(new IssueDependenciesDetected(
                repository.Id,
                issueNumber,
                dependencySuccess.Value));
        }
    }

    private static bool DetectNewIssues(
        MonitoredRepository repository,
        IReadOnlyList<ProviderIssue> fetchedIssues,
        IReadOnlySet<int> knownNumbers,
        DateTimeOffset now)
    {
        bool detected = false;

        foreach (ProviderIssue issue in fetchedIssues)
        {
            if (!knownNumbers.Contains(issue.Number))
            {
                repository.RecordIntegrationEvent(new IssueDetected(
                    repository.Id,
                    issue.Number,
                    issue.Title,
                    issue.Author,
                    issue.Url,
                    issue.Labels,
                    issue.IssueKindLabel,
                    now));
                detected = true;
            }
        }

        return detected;
    }

    private async Task<bool> DetectDetailChangesAsync(
        MonitoredRepository repository,
        IReadOnlyList<ProviderIssue> fetchedIssues,
        IReadOnlySet<int> knownNumbers,
        CancellationToken cancellationToken)
    {
        HashSet<int> knownFetchedNumbers = knownNumbers
            .Where(n => fetchedIssues.Any(i => i.Number == n))
            .ToHashSet();

        if (knownFetchedNumbers.Count == 0)
        {
            return false;
        }

        IReadOnlyDictionary<int, IssueSnapshot> snapshots = await issueQueries.GetIssueSnapshotsAsync(
            repository.Id,
            knownFetchedNumbers,
            cancellationToken);

        bool changed = false;

        foreach (ProviderIssue issue in fetchedIssues)
        {
            if (!snapshots.TryGetValue(issue.Number, out IssueSnapshot? snapshot))
            {
                continue;
            }

            if (HasDetailsChanged(snapshot, issue))
            {
                repository.RecordIntegrationEvent(new IssueDetailsChanged(
                    repository.Id,
                    issue.Number,
                    issue.Title,
                    issue.Labels));
                changed = true;
            }
        }

        return changed;
    }

    private async Task<bool> DetectReviewStatusChangesAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        int? issueNumber,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReviewIssueInfo> reviewIssues = await issueQueries.GetReviewIssuesAsync(
            repository.Id,
            cancellationToken);

        bool changed = false;

        // HTTP calls are sequential: GitHub has no batch issue/PR status endpoint,
        // and review issues per repository are expected to be small (design decision D6).
        foreach (ReviewIssueInfo reviewIssue in reviewIssues)
        {
            if (issueNumber is { } targetNumber && reviewIssue.IssueNumber != targetNumber)
            {
                continue;
            }

            Result<bool> isClosedResult = await provider.IsIssueClosedAsync(
                repository.Slug,
                reviewIssue.IssueNumber,
                cancellationToken);

            if (isClosedResult is not Result<bool>.Success isClosedSuccess)
            {
                continue;
            }

            if (isClosedSuccess.Value)
            {
                repository.RecordIntegrationEvent(new ProviderIssueClosed(repository.Id, reviewIssue.IssueNumber));
                changed = true;
                continue;
            }

            Result<PullRequestStatus> prStatusResult = await provider.GetPullRequestStatusAsync(
                repository.Slug,
                reviewIssue.PullRequestUrl,
                cancellationToken);

            if (prStatusResult is not Result<PullRequestStatus>.Success prStatusSuccess)
            {
                continue;
            }

            if (prStatusSuccess.Value.IsClosed && !prStatusSuccess.Value.IsMerged)
            {
                repository.RecordIntegrationEvent(new ProviderPullRequestClosed(repository.Id, reviewIssue.IssueNumber));
                changed = true;
                continue;
            }

            if (prStatusSuccess.Value.IsClosed)
            {
                continue;
            }

            Result<ReviewFeedback> feedbackResult = await provider.GetReviewFeedbackAsync(
                repository.Slug,
                reviewIssue.PullRequestUrl,
                reviewIssue.FeedbackCutoffAt,
                cancellationToken);

            if (feedbackResult is not Result<ReviewFeedback>.Success feedbackSuccess)
            {
                continue;
            }

            if (feedbackSuccess.Value.HeldBackUntil is { } heldBackUntil)
            {
                // Comments still inside the quiet period would otherwise wait for the backstop poll:
                // re-run this issue's review pass once the newest of them becomes actionable (issue #702).
                // Same-key nudges coalesce, so a burst of held comments yields one follow-up run.
                nudgeQueue.TryEnqueue(new Nudge(
                    new NudgeKey(repository.Id, NudgePass.Review, reviewIssue.IssueNumber),
                    heldBackUntil));
            }

            if (feedbackSuccess.Value.Comments.Count > 0)
            {
                ReviewFeedback feedback = feedbackSuccess.Value;
                repository.RecordIntegrationEvent(new PullRequestChangesRequested(
                    repository.Id,
                    reviewIssue.IssueNumber,
                    feedback.Comments,
                    feedback.OmittedCommentCount,
                    feedback.NewestCommentAt));
                changed = true;
            }
        }

        return changed;
    }

    private async Task<bool> DetectChecksPendingStatusChangesAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        int? issueNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ChecksPendingIssueInfo> checksPendingIssues = await issueQueries.GetChecksPendingIssuesAsync(
            repository.Id,
            cancellationToken);

        bool changed = false;

        // HTTP calls are sequential: GitHub has no batch issue/PR status endpoint,
        // and checks-pending issues per repository are expected to be small (design decision D6).
        foreach (ChecksPendingIssueInfo checksPendingIssue in checksPendingIssues)
        {
            if (issueNumber is { } targetNumber && checksPendingIssue.IssueNumber != targetNumber)
            {
                continue;
            }

            // Apply the same closed/merged handling as the review pass.
            Result<bool> isClosedResult = await provider.IsIssueClosedAsync(
                repository.Slug,
                checksPendingIssue.IssueNumber,
                cancellationToken);

            if (isClosedResult is not Result<bool>.Success isClosedSuccess)
            {
                continue;
            }

            if (isClosedSuccess.Value)
            {
                repository.RecordIntegrationEvent(
                    new ProviderIssueClosed(repository.Id, checksPendingIssue.IssueNumber));
                changed = true;
                continue;
            }

            Result<PullRequestStatus> prStatusResult = await provider.GetPullRequestStatusAsync(
                repository.Slug,
                checksPendingIssue.PullRequestUrl,
                cancellationToken);

            if (prStatusResult is not Result<PullRequestStatus>.Success prStatusSuccess)
            {
                continue;
            }

            if (prStatusSuccess.Value.IsClosed && !prStatusSuccess.Value.IsMerged)
            {
                repository.RecordIntegrationEvent(
                    new ProviderPullRequestClosed(repository.Id, checksPendingIssue.IssueNumber));
                changed = true;
                continue;
            }

            if (prStatusSuccess.Value.IsClosed)
            {
                // PR merged — no event here; the issue completes on a later poll when the
                // provider issue closes (IsIssueClosedAsync -> ProviderIssueClosed).
                continue;
            }

            // PR is still open — read the checks verdict and apply the gate decision.
            Result<Checks> checksResult = await provider.GetChecksAsync(
                repository.Slug,
                checksPendingIssue.PullRequestUrl,
                cancellationToken);

            if (checksResult is not Result<Checks>.Success checksSuccess)
            {
                continue;
            }

            TimeSpan elapsed = now - checksPendingIssue.EnteredChecksPendingAt;

            string? outcome = ResolveChecksOutcome(checksSuccess.Value, elapsed);
            if (outcome is null)
            {
                // Issue stays in checks_pending — no event recorded this cycle.
                continue;
            }

            IReadOnlyList<FailingCheck> failingChecks = checksSuccess.Value is Checks.Red red
                ? red.FailingChecks
                : [];

            repository.RecordIntegrationEvent(new ChecksVerdictResolved(
                repository.Id,
                checksPendingIssue.IssueNumber,
                outcome,
                failingChecks));

            // Only a provider-driven verdict counts as a webhook-mapped change; the grace-window and
            // upper-bound exits are time-based (deadline nudges), never announced by a delivery.
            if (IsProviderDrivenVerdict(checksSuccess.Value))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static bool IsProviderDrivenVerdict(Checks verdict) =>
        verdict is Checks.Green or Checks.Red or Checks.Unfinished;

    /// <summary>
    /// Maps a <see cref="Checks"/> verdict and elapsed time to a <see cref="ChecksVerdictResolved"/>
    /// outcome discriminator, or <c>null</c> when the issue should remain in <c>checks_pending</c>.
    /// </summary>
    private static string? ResolveChecksOutcome(Checks verdict, TimeSpan elapsed) =>
        verdict switch
        {
            Checks.Green => "passed",
            Checks.Red => "still_failing",
            Checks.Unfinished => "unfinished",
            Checks.Unreadable => "unreadable",
            Checks.NoneDetected when elapsed >= ChecksNoneGraceWindow => "none_detected",
            Checks.NoneDetected => null,
            Checks.Pending when elapsed >= ChecksPendingUpperBound => "unfinished",
            Checks.Pending => null,
            // Checks has a private constructor — all variants are enumerated above.
            // This arm is unreachable; throwing ensures a future variant is never silently ignored.
            _ => throw new UnreachableException($"Unhandled Checks variant: {verdict.GetType().Name}"),
        };

    private static bool DetectUntrackedIssues(
        MonitoredRepository repository,
        IReadOnlySet<int> untrackableNumbers,
        HashSet<int> fetchedNumbers)
    {
        bool untracked = false;

        foreach (int issueNumber in untrackableNumbers)
        {
            if (!fetchedNumbers.Contains(issueNumber))
            {
                repository.RecordIntegrationEvent(new ProviderIssueUntracked(repository.Id, issueNumber));
                untracked = true;
            }
        }

        return untracked;
    }

    private static bool HasDetailsChanged(IssueSnapshot snapshot, ProviderIssue issue)
    {
        if (snapshot.Title != issue.Title)
        {
            return true;
        }

        return !snapshot.Labels.ToHashSet().SetEquals(issue.Labels);
    }
}
