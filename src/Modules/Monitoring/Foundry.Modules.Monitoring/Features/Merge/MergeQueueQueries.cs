using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Merge;

/// <summary>
/// Reads an issue's catch-up queue entry from the repository's <see cref="PendingMerge"/> rows, ordered and filtered
/// to held issues as <see cref="MergeQueue"/> picks its head, so the position matches the merge step's view.
/// </summary>
/// <remarks>
/// <see cref="IIssueQueries"/> is resolved on first use, not at construction: the Issues module's queries read this
/// query for the issue detail, so a constructor dependency would be a cycle.
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

        IIssueQueries issues = issueQueries();
        IReadOnlyList<MergePendingIssueInfo> pendingIssues = await issues.GetMergePendingIssuesAsync(
            repositoryId,
            cancellationToken);
        IReadOnlyList<MergeQueueIssueInfo> queueIssues = await issues.GetMergeQueueIssuesAsync(
            repositoryId,
            cancellationToken);

        return MergeQueue.From(repositoryId, rows)
            .EntryOf(issueNumber, MergeQueue.HeldIssueNumbers(pendingIssues, queueIssues));
    }
}
