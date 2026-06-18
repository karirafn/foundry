using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal sealed class IssueQueries(DbContext db, IRepositorySlugQueries slugQueries) : IIssueQueries
{
    private const string DetectedState = "detected";
    private const string IneligibleState = "ineligible";

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

    public async Task<IReadOnlyList<int>> GetDetectedAndIneligibleIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        return await db.Set<Issue>()
            .AsNoTracking()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .Where(i => EF.Property<string>(i, "state") == DetectedState
                || EF.Property<string>(i, "state") == IneligibleState)
            .Select(i => i.IssueNumber)
            .ToListAsync(cancellationToken);
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

        return issues
            .Select(i => new IssueSummary(
                Id: i.Id.Value,
                IssueNumber: i.IssueNumber,
                Title: i.Title,
                State: GetStateDiscriminator(i),
                RepositorySlug: slugs.TryGetValue(i.MonitoredRepositoryId, out string? slug) ? slug : string.Empty,
                DetectedAt: i.DetectedAt,
                Url: i.Url.Value.ToString(),
                FailureClassification: ClassifyFailure(i)))
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

        return new IssueSummary(
            Id: issue.Id.Value,
            IssueNumber: issue.IssueNumber,
            Title: issue.Title,
            State: GetStateDiscriminator(issue),
            RepositorySlug: repositorySlug,
            DetectedAt: issue.DetectedAt,
            Url: issue.Url.Value.ToString(),
            FailureClassification: ClassifyFailure(issue));
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
            IneligibleIssue => "ineligible",
            InProgressIssue => "in_progress",
            ReviewIssue => "review",
            UnchangedIssue => "unchanged",
            FailedIssue => "failed",
            ContinuableFailedIssue => "continuable_failed",
            ContinuationQueuedIssue => "continuation_queued",
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
            IneligibleIssue ineligible => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                Violations: ineligible.Violations
                    .Select(v => new EligibilityViolationDto(v.Rule, v.Description))
                    .ToList()),

            BlockedIssue blocked => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: blocked.BlockedBy,
                Violations: null),

            InProgressIssue inProgress => new IssueStateDetails(
                WorkerRunId: inProgress.WorkerRunId,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                Violations: null),

            ReviewIssue review => new IssueStateDetails(
                WorkerRunId: review.WorkerRunId,
                BranchName: review.BranchName,
                PullRequestUrl: review.PullRequestUrl,
                FeedbackCutoffAt: review.FeedbackCutoffAt,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                Violations: null),

            UnchangedIssue unchanged => new IssueStateDetails(
                WorkerRunId: unchanged.WorkerRunId,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                Violations: null),

            FailedIssue failed => new IssueStateDetails(
                WorkerRunId: failed.WorkerRunId,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: failed.FailureReason,
                FailedAt: failed.FailedAt,
                CompletedAt: null,
                BlockedBy: null,
                Violations: null),

            CompletedIssue completed => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: completed.BranchName,
                PullRequestUrl: completed.PullRequestUrl,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: completed.CompletedAt,
                BlockedBy: null,
                Violations: null),

            DismissedIssue dismissed => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: dismissed.CompletedAt,
                BlockedBy: null,
                Violations: null),

            RevisionQueuedIssue revisionQueued => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: revisionQueued.BranchName,
                PullRequestUrl: revisionQueued.PullRequestUrl,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                Violations: null),

            RevisionInProgressIssue revisionInProgress => new IssueStateDetails(
                WorkerRunId: revisionInProgress.WorkerRunId,
                BranchName: revisionInProgress.BranchName,
                PullRequestUrl: revisionInProgress.PullRequestUrl,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                Violations: null),

            RevisionFailedIssue revisionFailed => new IssueStateDetails(
                WorkerRunId: revisionFailed.WorkerRunId,
                BranchName: revisionFailed.BranchName,
                PullRequestUrl: revisionFailed.PullRequestUrl,
                FeedbackCutoffAt: null,
                FailureReason: revisionFailed.FailureReason,
                FailedAt: revisionFailed.FailedAt,
                CompletedAt: null,
                BlockedBy: null,
                Violations: null),

            ContinuableFailedIssue continuableFailed => new IssueStateDetails(
                WorkerRunId: continuableFailed.WorkerRunId,
                BranchName: continuableFailed.BranchName,
                PullRequestUrl: continuableFailed.PullRequestUrl.Length > 0 ? continuableFailed.PullRequestUrl : null,
                FeedbackCutoffAt: null,
                FailureReason: continuableFailed.FailureReason,
                FailedAt: continuableFailed.FailedAt,
                CompletedAt: null,
                BlockedBy: null,
                Violations: null),

            ContinuationQueuedIssue continuationQueued => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: continuationQueued.BranchName,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                Violations: null),

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
