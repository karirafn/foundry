using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Merge;

/// <summary>
/// Reads catch-up queue entries from the repository's <see cref="PendingMerge"/> rows, ordered and filtered
/// to held issues as <see cref="MergeQueue"/> picks its head, so the position matches the merge step's view.
/// </summary>
/// <remarks>
/// <see cref="IIssueQueries"/> is resolved on first use, not at construction: the Issues module's queries read this
/// query for the issue detail and summaries, so a constructor dependency would be a cycle.
/// </remarks>
internal sealed class MergeQueueQueries(DbContext dbContext, Func<IIssueQueries> issueQueries) : IMergeQueueQueries
{
    public async Task<MergeQueueEntry?> GetQueueEntryAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        List<PendingMerge> rows = await dbContext.Set<PendingMerge>()
            .AsNoTracking()
            .Where(m => m.MonitoredRepositoryId == repositoryId)
            .ToListAsync(cancellationToken);
        if (!rows.Any(m => m.IssueNumber == issueNumber))
        {
            return null;
        }

        return MergeQueue.From(repositoryId, rows)
            .EntryOf(issueNumber, await HeldIssueNumbersAsync(repositoryId, cancellationToken));
    }

    public async Task<IReadOnlySet<(MonitoredRepositoryId RepositoryId, int IssueNumber)>> GetWaitingIssuesAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken)
    {
        List<MonitoredRepositoryId> ids = [.. repositoryIds];
        List<PendingMerge> rows = await dbContext.Set<PendingMerge>()
            .AsNoTracking()
            .Where(m => ids.Contains(m.MonitoredRepositoryId))
            .ToListAsync(cancellationToken);

        HashSet<(MonitoredRepositoryId RepositoryId, int IssueNumber)> waiting = [];

        // A repository with no waiting row has no waiting entry, so its issues are not read.
        foreach (IGrouping<MonitoredRepositoryId, PendingMerge> repositoryRows in rows
                     .GroupBy(m => m.MonitoredRepositoryId)
                     .Where(g => g.Any(m => m.AwaitsCatchUp)))
        {
            IReadOnlySet<int> held = await HeldIssueNumbersAsync(repositoryRows.Key, cancellationToken);
            IReadOnlyDictionary<int, MergeQueueEntry> entries = MergeQueue.From(repositoryRows.Key, repositoryRows)
                .Entries(held);
            waiting.UnionWith(entries
                .Where(e => e.Value.Wait is not null)
                .Select(e => (repositoryRows.Key, e.Key)));
        }

        return waiting;
    }

    private async Task<IReadOnlySet<int>> HeldIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        IIssueQueries issues = issueQueries();
        IReadOnlyList<MergePendingIssueInfo> pendingIssues = await issues.GetMergePendingIssuesAsync(
            repositoryId,
            cancellationToken);
        IReadOnlyList<MergeQueueIssueInfo> queueIssues = await issues.GetMergeQueueIssuesAsync(
            repositoryId,
            cancellationToken);

        return MergeQueue.HeldIssueNumbers(pendingIssues, queueIssues);
    }
}
