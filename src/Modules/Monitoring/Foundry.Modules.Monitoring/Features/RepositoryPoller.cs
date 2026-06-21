using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class RepositoryPoller(
    IIssueQueries issueQueries,
    DbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher,
    IIntegrationEventDispatcher integrationEventDispatcher,
    IRepositoryEligibilityEvaluator eligibilityEvaluator)
{
    public async Task<Result> PollAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Pass 0: evaluate repository-level eligibility unconditionally every poll cycle.
        await eligibilityEvaluator.EvaluateAndStoreAsync(repository, provider, cancellationToken);

        IReadOnlySet<int> knownNumbers = await issueQueries.GetKnownIssueNumbersAsync(
            repository.Id,
            cancellationToken);

        Result<IReadOnlyList<ProviderIssue>> providerResult = await provider.GetIssuesAsync(
            repository.Slug,
            cancellationToken);

        if (providerResult is not Result<IReadOnlyList<ProviderIssue>>.Success providerSuccess)
        {
            if (providerResult is Result<IReadOnlyList<ProviderIssue>>.Failure f)
            {
                return f.Error;
            }

            return new Error("Monitoring.UnexpectedResult", "GetIssues returned an unexpected result type.");
        }

        IReadOnlyList<ProviderIssue> fetchedIssues = providerSuccess.Value;

        DetectNewIssues(repository, fetchedIssues, knownNumbers, now);
        await DetectDetailChangesAsync(repository, fetchedIssues, knownNumbers, cancellationToken);

        repository.MarkPolled(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await domainEventDispatcher.DispatchAsync(repository.DomainEvents, cancellationToken);
        await integrationEventDispatcher.DispatchAsync(repository.IntegrationEvents, cancellationToken);
        repository.ClearDomainEvents();
        repository.ClearIntegrationEvents();

        // Pass 3: detect dependencies for all known non-terminal issues.
        // Re-query known numbers so newly detected issues from pass 1 are included.
        IReadOnlySet<int> knownNumbersForDependencies = await issueQueries.GetKnownIssueNumbersAsync(
            repository.Id,
            cancellationToken);

        await DetectDependenciesAsync(repository, provider, knownNumbersForDependencies, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await domainEventDispatcher.DispatchAsync(repository.DomainEvents, cancellationToken);
        await integrationEventDispatcher.DispatchAsync(repository.IntegrationEvents, cancellationToken);
        repository.ClearDomainEvents();
        repository.ClearIntegrationEvents();

        // Pass 4: check provider-side status of all review issues.
        await DetectReviewStatusChangesAsync(repository, provider, cancellationToken);

        await integrationEventDispatcher.DispatchAsync(repository.IntegrationEvents, cancellationToken);
        repository.ClearIntegrationEvents();

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
