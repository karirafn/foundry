using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal sealed class TriageFailedIssueQueries(DbContext db) : ITriageFailedIssueQueries
{
    public async Task<IReadOnlySet<int>> GetTriageFailedIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        List<int> numbers = await db.Set<TriageFailedIssue>()
            .AsNoTracking()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .Select(i => i.IssueNumber)
            .ToListAsync(cancellationToken);

        return numbers.ToHashSet();
    }
}
