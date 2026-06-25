using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal sealed class IssueQueries(
    DbContext db,
    IRepositorySlugQueries slugQueries,
    IRepositoryEligibilityQuery eligibilityQuery) : IIssueQueries
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

    public async Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        // EF Core cannot translate a C# instance method call (Issue.IsRestingState()) into SQL,
        // so the type-pattern Where clause is kept explicit here. A unit test in
        // GetUntrackableIssueNumbersAsync.cs asserts that both sets agree, so any future drift
        // between this expression and Issue.IsRestingState() fails a test.
        List<int> numbers = await db.Set<Issue>()
            .AsNoTracking()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .Where(i =>
                i is DetectedIssue ||
                i is QueuedIssue ||
                i is BlockedIssue ||
                i is FailedIssue ||
                i is ContinuableFailedIssue ||
                i is RevisionFailedIssue ||
                i is RevisionQueuedIssue ||
                i is ContinuationQueuedIssue)
            .Select(i => i.IssueNumber)
            .ToListAsync(cancellationToken);

        return numbers.ToHashSet();
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

        List<Issue> issues = await query
            .OrderByDescending(i => i.DetectedAt)
            .ToListAsync(cancellationToken);

        if (issues.Count == 0)
        {
            return [];
        }

        HashSet<MonitoredRepositoryId> repositoryIds = issues
            .Select(i => i.MonitoredRepositoryId)
            .ToHashSet();

        IReadOnlyDictionary<MonitoredRepositoryId, string> slugs = await slugQueries.GetSlugsAsync(
            repositoryIds,
            cancellationToken);

        HashSet<Guid> repositoryGuids = repositoryIds
            .Select(id => id.Value)
            .ToHashSet();

        IReadOnlyDictionary<Guid, string> eligibilityStatuses = await eligibilityQuery.GetEligibilityStatusesAsync(
            repositoryGuids,
            cancellationToken);

        return issues
            .Select(i => new IssueSummary(
                Id: i.Id.Value,
                IssueNumber: i.IssueNumber,
                Title: i.Title,
                State: GetStateDiscriminator(i),
                RepositorySlug: slugs.TryGetValue(i.MonitoredRepositoryId, out string? slug) ? slug : string.Empty,
                DetectedAt: i.DetectedAt,
                Url: i.Url.Value.ToString(),
                FailureClassification: ClassifyFailure(i),
                RepositoryEligibilityStatus: eligibilityStatuses.TryGetValue(i.MonitoredRepositoryId.Value, out string? status)
                    ? status
                    : null))
            .ToList();
    }

    public async Task<IssueSummary?> GetIssueSummaryAsync(
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

        HashSet<Guid> repoGuids = [issue.MonitoredRepositoryId.Value];
        IReadOnlyDictionary<Guid, string> eligibilityStatuses = await eligibilityQuery.GetEligibilityStatusesAsync(
            repoGuids,
            cancellationToken);

        string? eligibilityStatus = eligibilityStatuses.TryGetValue(issue.MonitoredRepositoryId.Value, out string? repoStatus)
            ? repoStatus
            : null;

        return new IssueSummary(
            Id: issue.Id.Value,
            IssueNumber: issue.IssueNumber,
            Title: issue.Title,
            State: GetStateDiscriminator(issue),
            RepositorySlug: repositorySlug,
            DetectedAt: issue.DetectedAt,
            Url: issue.Url.Value.ToString(),
            FailureClassification: ClassifyFailure(issue),
            RepositoryEligibilityStatus: eligibilityStatus);
    }

    private static string? ClassifyFailure(Issue issue)
    {
        string? failureReason = issue switch
        {
            FailedIssue failed => failed.FailureReason,
            ContinuableFailedIssue continuableFailed => continuableFailed.FailureReason,
            _ => null,
        };

        if (failureReason is null)
        {
            return null;
        }

        return failureReason.StartsWith(WorkerRunFailed.UsageLimitedReason, StringComparison.OrdinalIgnoreCase)
            ? "usage_limited"
            : null;
    }

    public async Task<Result<IssueDetail>> GetIssueDetailAsync(
        IssueId issueId,
        CancellationToken cancellationToken)
    {
        // Full entity load is required here — EF cannot translate the is-pattern discriminator
        // matching used in BuildStateDetails into SQL, so projection is not feasible.
        Issue? issue = await db.Set<Issue>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        if (issue is null)
        {
            return Result<IssueDetail>.Fail(IssueErrors.NotFound(issueId));
        }

        HashSet<MonitoredRepositoryId> repoIds = [issue.MonitoredRepositoryId];
        IReadOnlyDictionary<MonitoredRepositoryId, string> slugs = await slugQueries.GetSlugsAsync(
            repoIds,
            cancellationToken);

        string repositorySlug = slugs.TryGetValue(issue.MonitoredRepositoryId, out string? slug)
            ? slug
            : string.Empty;

        string? providerType = await slugQueries.GetProviderTypeAsync(
            issue.MonitoredRepositoryId,
            cancellationToken);

        string state = GetStateDiscriminator(issue);

        IssueStateDetails? stateDetails = BuildStateDetails(issue);

        return new IssueDetail(
            Id: issue.Id.Value,
            IssueNumber: issue.IssueNumber,
            Title: issue.Title,
            State: state,
            RepositorySlug: repositorySlug,
            ProviderType: providerType ?? string.Empty,
            DetectedAt: issue.DetectedAt,
            Url: issue.Url.Value.ToString(),
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
            ContinuableFailedIssue => "continuable_failed",
            ContinuationQueuedIssue => "continuation_queued",
            CompletedIssue => "completed",
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

            ContinuableFailedIssue continuableFailed => new IssueStateDetails(
                WorkerRunId: continuableFailed.WorkerRunId,
                BranchName: continuableFailed.BranchName,
                PullRequestUrl: continuableFailed.PullRequestUrl.Length > 0 ? continuableFailed.PullRequestUrl : null,
                FeedbackCutoffAt: null,
                FailureReason: continuableFailed.FailureReason,
                FailedAt: continuableFailed.FailedAt,
                CompletedAt: null,
                BlockedBy: null),

            ContinuationQueuedIssue continuationQueued => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: continuationQueued.BranchName,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null),

            _ => null
        };

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
