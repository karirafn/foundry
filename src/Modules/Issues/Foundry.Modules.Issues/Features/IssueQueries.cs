using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal sealed class IssueQueries(DbContext db, IRepositorySlugQueries slugQueries) : IIssueQueries
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

    public async Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        return await db.Set<ReviewIssue>()
            .AsNoTracking()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .Select(i => new ReviewIssueInfo(i.IssueNumber, i.PullRequestUrl, i.FeedbackCutoffAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
        MonitoredRepositoryId? repositoryId,
        CancellationToken cancellationToken)
    {
        IQueryable<Issue> query = db.Set<Issue>()
            .AsNoTracking();

        if (repositoryId is not null)
        {
            query = query.Where(i => i.MonitoredRepositoryId == repositoryId);
        }

        List<IssueProjection> projections = await query
            .OrderByDescending(i => i.DetectedAt)
            .Select(i => new IssueProjection(
                i.Id,
                i.IssueNumber,
                i.Title,
                EF.Property<string>(i, "state"),
                i.MonitoredRepositoryId,
                i.DetectedAt,
                i.Url))
            .ToListAsync(cancellationToken);

        if (projections.Count == 0)
        {
            return [];
        }

        HashSet<MonitoredRepositoryId> repositoryIds = projections
            .Select(p => p.MonitoredRepositoryId)
            .ToHashSet();

        IReadOnlyDictionary<MonitoredRepositoryId, string> slugs = await slugQueries.GetSlugsAsync(
            repositoryIds,
            cancellationToken);

        return projections
            .Select(p => new IssueSummary(
                Id: p.Id.Value,
                IssueNumber: p.IssueNumber,
                Title: p.Title,
                State: p.State,
                RepositorySlug: slugs.TryGetValue(p.MonitoredRepositoryId, out string? slug) ? slug : string.Empty,
                DetectedAt: p.DetectedAt,
                Url: p.Url.Value.ToString()))
            .ToList();
    }

    private sealed record IssueProjection(
        IssueId Id,
        int IssueNumber,
        string Title,
        string State,
        MonitoredRepositoryId MonitoredRepositoryId,
        DateTimeOffset DetectedAt,
        ProviderUrl Url);

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
