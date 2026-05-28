using Foundry.WebApi.Modules.Issues;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.Shared.Persistence;

namespace Foundry.WebApi.Modules.Monitoring.Features;

public sealed class RepositoryPoller(
    IIssuesModule issuesModule,
    FoundryDbContext dbContext,
    IDomainEventDispatcher eventDispatcher)
{
    public async Task<Result> PollAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<int> knownNumbers = await issuesModule.GetKnownIssueNumbersAsync(
            repository.Id,
            cancellationToken);

        Result<IReadOnlyList<ProviderIssue>> providerResult = await provider.GetIssuesAsync(
            repository.Slug,
            cancellationToken);

        if (providerResult is not Result<IReadOnlyList<ProviderIssue>>.Success providerSuccess)
        {
            return ((Result<IReadOnlyList<ProviderIssue>>.Failure)providerResult).Error;
        }

        IReadOnlyList<ProviderIssue> fetchedIssues = providerSuccess.Value;

        DetectNewIssues(repository, fetchedIssues, knownNumbers, now);
        await DetectDetailChangesAsync(repository, fetchedIssues, knownNumbers, cancellationToken);

        repository.MarkPolled(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await eventDispatcher.DispatchAsync(repository.DomainEvents, cancellationToken);
        repository.ClearDomainEvents();

        // Pass 3: detect dependencies for all known non-terminal issues.
        // Re-query known numbers so newly detected issues from pass 1 are included.
        IReadOnlySet<int> knownNumbersForDependencies = await issuesModule.GetKnownIssueNumbersAsync(
            repository.Id,
            cancellationToken);

        await DetectDependenciesAsync(repository, provider, knownNumbersForDependencies, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await eventDispatcher.DispatchAsync(repository.DomainEvents, cancellationToken);
        repository.ClearDomainEvents();

        return Result.Ok();
    }

    private static async Task DetectDependenciesAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        IReadOnlySet<int> issueNumbers,
        CancellationToken cancellationToken)
    {
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

            repository.RecordDomainEvent(new IssueDependenciesDetected(
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
                repository.RecordDomainEvent(new IssueDetected(
                    repository.Id,
                    issue.Number,
                    issue.Title,
                    issue.Body,
                    issue.Author,
                    issue.Url,
                    issue.Labels,
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

        IReadOnlyDictionary<int, IssueSnapshot> snapshots = await issuesModule.GetIssueSnapshotsAsync(
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
                repository.RecordDomainEvent(new IssueDetailsChanged(
                    repository.Id,
                    issue.Number,
                    issue.Title,
                    issue.Body,
                    issue.Labels));
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
