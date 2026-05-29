using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal sealed class IssueQueries(DbContext db) : IIssueQueries
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
}
