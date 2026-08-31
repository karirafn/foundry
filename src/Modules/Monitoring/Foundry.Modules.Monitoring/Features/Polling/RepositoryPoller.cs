using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
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
    ILogger<RepositoryPoller> logger)
{
    /// <summary>
    /// The fixed per-cycle provider call budget that does NOT scale with issue count:
    /// one eligibility branch-rules GET plus one <see cref="IIssueProvider.GetIssuesAsync"/> listing call.
    /// This applies when the repository's write-probe verdict is Granted (the cheap eligibility path),
    /// there are zero review issues, and zero dependency candidates.
    /// Referenced by ADR 0066 and DOMAIN.md; asserted by the poll-call invariance test.
    /// </summary>
    internal const int MaxFixedPollCallsPerCycle = 2;

    public async Task<Result> PollAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Pass 0: evaluate repository-level eligibility every poll cycle.
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
                return f.Error;
            }

            return new Error("Monitoring.UnexpectedResult", "GetIssues returned an unexpected result type.");
        }

        IssueListing listing = providerSuccess.Value;
        IReadOnlyList<ProviderIssue> fetchedIssues = listing.Issues;

        HashSet<int> fetchedNumbers = fetchedIssues
            .Select(i => i.Number)
            .ToHashSet();

        DetectNewIssues(repository, fetchedIssues, knownNumbers, now);
        await DetectDetailChangesAsync(repository, fetchedIssues, knownNumbers, cancellationToken);

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

            DetectUntrackedIssues(repository, untrackableNumbers, fetchedNumbers);
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

        repository.MarkPolled(now);
        await integrationEventDispatcher.DispatchAsync(repository.IntegrationEvents, cancellationToken);
        repository.ClearIntegrationEvents();
        await dbContext.SaveChangesAsync(cancellationToken);
        await domainEventDispatcher.DispatchAsync(repository.DomainEvents, cancellationToken);
        repository.ClearDomainEvents();

        // Pass 3: detect dependencies for the dispatch-candidate issues the dependency handler can act on.
        // Re-query after the first save so issues newly detected in this cycle are included.
        IReadOnlySet<int> candidateNumbers = await issueQueries.GetDispatchCandidateIssueNumbersAsync(
            repository.Id,
            cancellationToken);

        await DetectDependenciesAsync(repository, provider, candidateNumbers, cancellationToken);

        await integrationEventDispatcher.DispatchAsync(repository.IntegrationEvents, cancellationToken);
        repository.ClearIntegrationEvents();
        await dbContext.SaveChangesAsync(cancellationToken);
        await domainEventDispatcher.DispatchAsync(repository.DomainEvents, cancellationToken);
        repository.ClearDomainEvents();

        // Pass 4: check provider-side status of all review issues.
        await DetectReviewStatusChangesAsync(repository, provider, cancellationToken);

        await integrationEventDispatcher.DispatchAsync(repository.IntegrationEvents, cancellationToken);
        repository.ClearIntegrationEvents();
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Ok();
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

    private static void DetectNewIssues(
        MonitoredRepository repository,
        IReadOnlyList<ProviderIssue> fetchedIssues,
        IReadOnlySet<int> knownNumbers,
        DateTimeOffset now)
    {
        foreach (ProviderIssue issue in fetchedIssues)
        {
            if (!knownNumbers.Contains(issue.Number))
            {
                repository.RecordIntegrationEvent(new IssueDetected(
                    repository.Id,
                    issue.Number,
                    issue.Title,
                    issue.Body,
                    issue.Author,
                    issue.Url,
                    issue.Labels,
                    issue.IssueKindLabel,
                    now));
            }
        }
    }

    private async Task DetectDetailChangesAsync(
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
            return;
        }

        IReadOnlyDictionary<int, IssueSnapshot> snapshots = await issueQueries.GetIssueSnapshotsAsync(
            repository.Id,
            knownFetchedNumbers,
            cancellationToken);

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
                    issue.Body,
                    issue.Labels));
            }
        }
    }

    private async Task DetectReviewStatusChangesAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReviewIssueInfo> reviewIssues = await issueQueries.GetReviewIssuesAsync(
            repository.Id,
            cancellationToken);

        // HTTP calls are sequential: GitHub has no batch issue/PR status endpoint,
        // and review issues per repository are expected to be small (design decision D6).
        foreach (ReviewIssueInfo reviewIssue in reviewIssues)
        {
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

            if (feedbackSuccess.Value.Comments.Count > 0)
            {
                repository.RecordIntegrationEvent(new PullRequestChangesRequested(
                    repository.Id,
                    reviewIssue.IssueNumber,
                    feedbackSuccess.Value.Comments));
            }
        }
    }

    private static void DetectUntrackedIssues(
        MonitoredRepository repository,
        IReadOnlySet<int> untrackableNumbers,
        HashSet<int> fetchedNumbers)
    {
        foreach (int issueNumber in untrackableNumbers)
        {
            if (!fetchedNumbers.Contains(issueNumber))
            {
                repository.RecordIntegrationEvent(new ProviderIssueUntracked(repository.Id, issueNumber));
            }
        }
    }

    private static bool HasDetailsChanged(IssueSnapshot snapshot, ProviderIssue issue)
    {
        if (snapshot.Title != issue.Title)
        {
            return true;
        }

        if (snapshot.Body != issue.Body)
        {
            return true;
        }

        return !snapshot.Labels.ToHashSet().SetEquals(issue.Labels);
    }
}
