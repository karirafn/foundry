using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features.RepositoryReactions;

internal sealed class RepositoryRemovalBlockers(DbContext db, IWorkerRunQueries workerRuns) : IRepositoryRemovalBlockers
{
    public async Task<IReadOnlyList<RemovalBlockingIssue>> GetBlockingIssuesAsync(
        IReadOnlyCollection<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken)
    {
        if (repositoryIds.Count == 0)
        {
            return [];
        }

        List<MonitoredRepositoryId> ids = [.. repositoryIds];
        List<Issue> inFlight = await db.Set<Issue>()
            .AsNoTracking()
            .Where(i => ids.Contains(i.MonitoredRepositoryId))
            .Where(i => i is TriageInProgressIssue
                || i is InProgressIssue
                || i is RevisionInProgressIssue
                || i is ReviewInProgressIssue)
            .ToListAsync(cancellationToken);

        if (inFlight.Count == 0)
        {
            return [];
        }

        IReadOnlySet<Guid> waitingForRetry = await workerRuns.GetIssueIdsWaitingForPublishRetryAsync(
            [.. inFlight.Select(i => i.Id.Value)],
            cancellationToken);

        return
        [
            .. inFlight
                .Where(i => !waitingForRetry.Contains(i.Id.Value))
                .OrderBy(i => i.MonitoredRepositoryId.Value)
                .ThenBy(i => i.IssueNumber)
                .Select(i => new RemovalBlockingIssue(i.MonitoredRepositoryId, i.IssueNumber, StateOf(i))),
        ];
    }

    private static string StateOf(Issue issue) =>
        issue switch
        {
            TriageInProgressIssue => "triage_in_progress",
            InProgressIssue => "in_progress",
            RevisionInProgressIssue => "revision_in_progress",
            ReviewInProgressIssue => "review_in_progress",
            _ => throw new InvalidOperationException($"Issue state {issue.GetType().Name} does not block removal."),
        };
}
