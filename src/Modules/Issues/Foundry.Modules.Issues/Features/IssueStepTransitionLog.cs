using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal static class IssueStepTransitionLog
{
    /// <summary>
    /// Marks the logs of <paramref name="issueIds"/> for deletion, so they go in the caller's save that deletes the
    /// issues. A transition also deletes an issue row, so the log cannot follow the issues table on its own.
    /// </summary>
    public static async Task RemoveLogsOfAsync(
        this DbContext db,
        IReadOnlyCollection<IssueId> issueIds,
        CancellationToken cancellationToken)
    {
        List<IssueStepTransition> rows = await db.Set<IssueStepTransition>()
            .Where(t => issueIds.Contains(t.IssueId))
            .ToListAsync(cancellationToken);
        db.Set<IssueStepTransition>().RemoveRange(rows);
    }
}
