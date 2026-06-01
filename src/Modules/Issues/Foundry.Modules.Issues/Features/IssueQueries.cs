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

    public async Task<IssueSummary?> GetIssueSummaryAsync(
        IssueId issueId,
        CancellationToken cancellationToken)
    {
        IssueProjection? projection = await db.Set<Issue>()
            .AsNoTracking()
            .Where(i => i.Id == issueId)
            .Select(i => new IssueProjection(
                i.Id,
                i.IssueNumber,
                i.Title,
                EF.Property<string>(i, "state"),
                i.MonitoredRepositoryId,
                i.DetectedAt,
                i.Url))
            .FirstOrDefaultAsync(cancellationToken);

        if (projection is null)
        {
            return null;
        }

        HashSet<MonitoredRepositoryId> repoIds = [projection.MonitoredRepositoryId];
        IReadOnlyDictionary<MonitoredRepositoryId, string> slugs = await slugQueries.GetSlugsAsync(
            repoIds,
            cancellationToken);

        string repositorySlug = slugs.TryGetValue(projection.MonitoredRepositoryId, out string? slug)
            ? slug
            : string.Empty;

        return new IssueSummary(
            Id: projection.Id.Value,
            IssueNumber: projection.IssueNumber,
            Title: projection.Title,
            State: projection.State,
            RepositorySlug: repositorySlug,
            DetectedAt: projection.DetectedAt,
            Url: projection.Url.Value.ToString());
    }

    public async Task<IssueDetail?> GetIssueDetailAsync(
        IssueId issueId,
        CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        if (issue is null)
        {
            return null;
        }

        HashSet<MonitoredRepositoryId> repoIds = [issue.MonitoredRepositoryId];
        IReadOnlyDictionary<MonitoredRepositoryId, string> slugs = await slugQueries.GetSlugsAsync(
            repoIds,
            cancellationToken);

        string repositorySlug = slugs.TryGetValue(issue.MonitoredRepositoryId, out string? slug)
            ? slug
            : string.Empty;

        string state = GetStateDiscriminator(issue);

        IssueStateDetails? stateDetails = BuildStateDetails(issue);

        return new IssueDetail(
            Id: issue.Id.Value,
            IssueNumber: issue.IssueNumber,
            Title: issue.Title,
            State: state,
            RepositorySlug: repositorySlug,
            DetectedAt: issue.DetectedAt,
            Url: issue.Url.Value.ToString(),
            Body: issue.Body,
            Author: issue.Author.Value,
            Labels: issue.Labels,
            StateDetails: stateDetails);
    }

    private static string GetStateDiscriminator(Issue issue) =>
        issue switch
        {
            DetectedIssue => "detected",
            QueuedIssue => "queued",
            BlockedIssue => "blocked",
            InProgressIssue => "in_progress",
            ReviewIssue => "review",
            UnchangedIssue => "unchanged",
            FailedIssue => "failed",
            CompletedIssue => "completed",
            DismissedIssue => "dismissed",
            RevisionQueuedIssue => "revision_queued",
            RevisionInProgressIssue => "revision_in_progress",
            RevisionFailedIssue => "revision_failed",
            _ => throw new InvalidOperationException($"Unknown issue type: {issue.GetType().Name}")
        };

    private static IssueStateDetails? BuildStateDetails(Issue issue) =>
        issue switch
        {
            BlockedIssue blocked => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: blocked.BlockedBy),

            InProgressIssue inProgress => new IssueStateDetails(
                WorkerRunId: inProgress.WorkerRunId,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null),

            ReviewIssue review => new IssueStateDetails(
                WorkerRunId: review.WorkerRunId,
                BranchName: review.BranchName,
                PullRequestUrl: review.PullRequestUrl,
                FeedbackCutoffAt: review.FeedbackCutoffAt,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null),

            UnchangedIssue unchanged => new IssueStateDetails(
                WorkerRunId: unchanged.WorkerRunId,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null),

            FailedIssue failed => new IssueStateDetails(
                WorkerRunId: failed.WorkerRunId,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: failed.FailureReason,
                FailedAt: failed.FailedAt,
                CompletedAt: null,
                BlockedBy: null),

            CompletedIssue completed => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: completed.BranchName,
                PullRequestUrl: completed.PullRequestUrl,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: completed.CompletedAt,
                BlockedBy: null),

            DismissedIssue dismissed => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: dismissed.CompletedAt,
                BlockedBy: null),

            RevisionQueuedIssue revisionQueued => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: revisionQueued.BranchName,
                PullRequestUrl: revisionQueued.PullRequestUrl,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null),

            RevisionInProgressIssue revisionInProgress => new IssueStateDetails(
                WorkerRunId: revisionInProgress.WorkerRunId,
                BranchName: revisionInProgress.BranchName,
                PullRequestUrl: revisionInProgress.PullRequestUrl,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null),

            RevisionFailedIssue revisionFailed => new IssueStateDetails(
                WorkerRunId: revisionFailed.WorkerRunId,
                BranchName: revisionFailed.BranchName,
                PullRequestUrl: revisionFailed.PullRequestUrl,
                FeedbackCutoffAt: null,
                FailureReason: revisionFailed.FailureReason,
                FailedAt: revisionFailed.FailedAt,
                CompletedAt: null,
                BlockedBy: null),

            _ => null
        };

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
