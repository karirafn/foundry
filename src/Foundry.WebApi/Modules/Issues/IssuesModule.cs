using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Issues.Features;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.WebApi.Modules.Issues;

internal sealed class IssuesModule(FoundryDbContext db) : IIssuesModule
{
    public async Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        List<int> numbers = await db.Set<Issue>()
            .AsNoTracking()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .Select(i => i.IssueNumber)
            .ToListAsync(cancellationToken);

        return numbers.ToHashSet();
    }

    public async Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
        MonitoredRepositoryId repositoryId,
        IReadOnlySet<int> issueNumbers,
        CancellationToken cancellationToken)
    {
        List<int> numberList = issueNumbers.ToList();

        Dictionary<int, IssueSnapshot> snapshots = await db.Set<Issue>()
            .AsNoTracking()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .Where(i => numberList.Contains(i.IssueNumber))
            .Select(i => new { i.IssueNumber, Snapshot = new IssueSnapshot(i.Title, i.Body, i.Labels) })
            .ToDictionaryAsync(x => x.IssueNumber, x => x.Snapshot, cancellationToken);

        return snapshots;
    }

    public async Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        List<Issue> allIssues = await db.Set<Issue>()
            .AsNoTracking()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .ToListAsync(cancellationToken);

        List<DependencyEdge> edges = [];
        foreach (Issue issue in allIssues.Where(i => i.BlockedBy.Count > 0))
        {
            foreach (int blockerNumber in issue.BlockedBy)
            {
                edges.Add(new DependencyEdge(issue.IssueNumber, blockerNumber));
            }
        }

        return edges;
    }

    public async Task<ClaimedIssueDispatch?> ClaimNextQueuedIssueAsync(
        Guid workerRunId,
        CancellationToken cancellationToken)
    {
        // Claim the oldest queued issue (FIFO). The BackgroundService processes claims
        // sequentially, so any queued issue is a valid next candidate — no ordering required.
        QueuedIssue? queued = await db.Set<QueuedIssue>()
            .FirstOrDefaultAsync(cancellationToken);

        if (queued is null)
        {
            return null;
        }

        var dispatchData = await db.Set<MonitoredRepository>()
            .Where(r => r.Id == queued.MonitoredRepositoryId)
            .Join(
                db.Set<Account>(),
                r => r.AccountId,
                a => a.Id,
                (r, a) => new { r.Slug, a.SecretKeyName, a.BaseUrl })
            .FirstOrDefaultAsync(cancellationToken);

        if (dispatchData is null)
        {
            return null;
        }

        InProgressIssue inProgress = queued.Claim(workerRunId);
        await db.TransitionAsync(queued, inProgress, cancellationToken);

        Uri cloneUrl = new(dispatchData.BaseUrl, $"{dispatchData.Slug}.git");

        return new ClaimedIssueDispatch(
            inProgress.Id,
            inProgress.IssueNumber,
            inProgress.Title,
            inProgress.Body,
            dispatchData.Slug.ToString(),
            cloneUrl,
            dispatchData.SecretKeyName);
    }
}

public static class IssuesModuleServiceCollectionExtensions
{
    public static IServiceCollection AddIssuesModule(this IServiceCollection services)
    {
        services.AddScoped<IIssuesModule, IssuesModule>();
        services.AddDomainEventHandler<IssueDetected, CreateIssueHandler>();
        services.AddDomainEventHandler<IssueDetailsChanged, UpdateIssueDetailsHandler>();
        services.AddDomainEventHandler<IssueDependenciesDetected, ProcessIssueDependenciesHandler>();
        return services;
    }
}
