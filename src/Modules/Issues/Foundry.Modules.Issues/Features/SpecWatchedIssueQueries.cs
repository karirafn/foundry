using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal sealed class SpecWatchedIssueQueries(DbContext db) : ISpecWatchedIssueQueries
{
    public async Task<IReadOnlyList<SpecWatchedIssue>> GetSpecWatchedIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken) =>
        await db.Set<Issue>()
            .AsNoTracking()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .Where(i => i is RevisionQueuedIssue
                || i is RevisionBlockedIssue
                || i is ContinuationQueuedIssue
                || i is ContinuationBlockedIssue
                || i is ReviewIssue
                || i is RevisionFailedIssue)
            .OrderBy(i => i.IssueNumber)
            .Select(i => new SpecWatchedIssue(i.IssueNumber, i.TriagedSpecHash))
            .ToListAsync(cancellationToken);
}
