using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

/// <summary>Reads an issue's step transition log.</summary>
internal sealed class IssueStepTransitionQueries(DbContext db)
{
    /// <summary>The issue's log in entry order, oldest first; empty for an issue that does not exist.</summary>
    public async Task<IReadOnlyList<IssueStepTransition>> GetForIssueAsync(IssueId issueId, CancellationToken cancellationToken) =>
        await db.Set<IssueStepTransition>()
            .AsNoTracking()
            .Where(t => t.IssueId == issueId)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);
}
