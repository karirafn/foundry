using System.Linq.Expressions;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.StateChanges;
using Foundry.Modules.Issues.Features.TransientRetry;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal sealed class IssueQueries(
    DbContext db,
    IRepositorySlugQueries slugQueries,
    IRepositoryEligibilityQuery eligibilityQuery,
    IWorkerRunQueries workerRunQueries) : IIssueQueries
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

    public async Task<IReadOnlySet<int>> GetDispatchCandidateIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        // EF Core cannot translate a C# instance method call into SQL, so the type-pattern
        // Where clause is kept explicit here. Only the three concrete states that
        // ProcessIssueDependenciesHandler acts on are included — FreshQueuedIssue is used
        // explicitly (never the QueuedIssue base type) because RevisionQueuedIssue and
        // ContinuationQueuedIssue both derive from QueuedIssue but are not acted on.
        List<int> numbers = await db.Set<Issue>()
            .AsNoTracking()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .Where(i =>
                i is DetectedIssue ||
                i is FreshQueuedIssue ||
                i is BlockedIssue)
            .Select(i => i.IssueNumber)
            .ToListAsync(cancellationToken);

        return numbers.ToHashSet();
    }

    public async Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        // EF Core cannot translate a C# instance method call (Issue.IsRestingState()) into SQL,
        // so the type-pattern Where clause is kept explicit here. A unit test in
        // GetUntrackableIssueNumbersAsync.cs asserts that both sets agree, so any future drift
        // between this expression and Issue.IsRestingState() fails a test.
        // QueuedIssue covers all queued variants — EF Core translates `i is QueuedIssue`
        // to `state IN ('queued', 'revision_queued', 'continuation_queued')` via TPH discriminators.
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
                i is UnchangedIssue)
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

        return await EnrichAsync(issues, cancellationToken);
    }

    private async Task<IReadOnlyList<IssueSummary>> EnrichAsync(
        List<Issue> issues,
        CancellationToken cancellationToken)
    {
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

        List<Guid> issueIds = issues
            .Select(i => i.Id.Value)
            .ToList();

        IReadOnlyDictionary<Guid, RunAggregate> runAggregates = await workerRunQueries.GetRunAggregatesForIssuesAsync(
            issueIds,
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
                FailureClassification: GetFailureCategory(i),
                RepositoryEligibilityStatus: eligibilityStatuses.TryGetValue(i.MonitoredRepositoryId.Value, out string? status)
                    ? status
                    : null,
                RunStats: runAggregates.TryGetValue(i.Id.Value, out RunAggregate? aggregate)
                    ? MapRunStats(aggregate)
                    : null))
            .ToList();
    }

    private static RunStats MapRunStats(RunAggregate aggregate) =>
        new(
            RunCount: aggregate.RunCount,
            DurationMs: aggregate.DurationMs,
            NumTurns: aggregate.NumTurns,
            TotalCostUsd: aggregate.TotalCostUsd,
            InputTokens: aggregate.InputTokens,
            OutputTokens: aggregate.OutputTokens);

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

        IReadOnlyDictionary<Guid, RunAggregate> runAggregates = await workerRunQueries.GetRunAggregatesForIssuesAsync(
            [issue.Id.Value],
            cancellationToken);

        return new IssueSummary(
            Id: issue.Id.Value,
            IssueNumber: issue.IssueNumber,
            Title: issue.Title,
            State: GetStateDiscriminator(issue),
            RepositorySlug: repositorySlug,
            DetectedAt: issue.DetectedAt,
            Url: issue.Url.Value.ToString(),
            FailureClassification: GetFailureCategory(issue),
            RepositoryEligibilityStatus: eligibilityStatus,
            RunStats: runAggregates.TryGetValue(issue.Id.Value, out RunAggregate? aggregate)
                ? MapRunStats(aggregate)
                : null);
    }

    private static string? GetFailureCategory(Issue issue) =>
        issue switch
        {
            FailedIssue failed => failed.FailureCategory.Length > 0 ? failed.FailureCategory : null,
            ContinuableFailedIssue continuableFailed => continuableFailed.FailureCategory.Length > 0 ? continuableFailed.FailureCategory : null,
            RevisionFailedIssue revisionFailed => revisionFailed.FailureCategory.Length > 0 ? revisionFailed.FailureCategory : null,
            _ => null,
        };

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

        TransientRetryDetails? transientRetry = await BuildTransientRetryDetailsAsync(issue, cancellationToken);
        IssueStateDetails? stateDetails = BuildStateDetails(issue, transientRetry);

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
            FreshQueuedIssue => "queued",
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

    private async Task<TransientRetryDetails?> BuildTransientRetryDetailsAsync(
        Issue issue,
        CancellationToken cancellationToken)
    {
        string? failureCategory = issue switch
        {
            FailedIssue failed => failed.FailureCategory,
            ContinuableFailedIssue continuableFailed => continuableFailed.FailureCategory,
            _ => null
        };

        if (failureCategory != TransientRetrySchedule.TransientApiErrorCategory)
        {
            return null;
        }

        // Retrieve the count of consecutive transient runs from the worker run store.
        // Returns 0 when the worker-run row is not yet visible (e.g. write not yet committed).
        int consecutiveRuns = await workerRunQueries.CountConsecutiveTransientRunsAsync(
            issue.Id.Value,
            TransientRetrySchedule.MaxTransientRetries,
            cancellationToken);

        // Guard: worker-run row not yet visible — surface no retry block rather than "Attempt 0 of N".
        if (consecutiveRuns <= 0)
        {
            return null;
        }

        int maxAttempts = TransientRetrySchedule.MaxTransientRetries;
        bool isExhausted = consecutiveRuns >= maxAttempts;

        // EF SQLite provider normalizes FailedAt to UTC — the derived NextAttemptDueAt relies on this.
        DateTimeOffset failedAt = issue switch
        {
            FailedIssue failed => failed.FailedAt,
            ContinuableFailedIssue continuableFailed => continuableFailed.FailedAt,
            _ => throw new InvalidOperationException($"Unexpected transient issue type: {issue.GetType().Name}")
        };

        return new TransientRetryDetails(
            AttemptNumber: consecutiveRuns,
            MaxAttempts: maxAttempts,
            IsExhausted: isExhausted,
            NextAttemptDueAt: isExhausted ? null : failedAt + TransientRetrySchedule.ComputeBackoff(consecutiveRuns));
    }

    private static IssueStateDetails? BuildStateDetails(Issue issue, TransientRetryDetails? transientRetry) =>
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
                BlockedBy: blocked.BlockedBy,
                TransientRetry: null),

            InProgressIssue inProgress => new IssueStateDetails(
                WorkerRunId: inProgress.WorkerRunId,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                TransientRetry: null),

            ReviewIssue review => new IssueStateDetails(
                WorkerRunId: review.WorkerRunId,
                BranchName: review.BranchName,
                PullRequestUrl: review.PullRequestUrl,
                FeedbackCutoffAt: review.FeedbackCutoffAt,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                TransientRetry: null),

            UnchangedIssue unchanged => new IssueStateDetails(
                WorkerRunId: unchanged.WorkerRunId,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                TransientRetry: null),

            FailedIssue failed => new IssueStateDetails(
                WorkerRunId: failed.WorkerRunId,
                BranchName: null,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: failed.FailureReason,
                FailedAt: failed.FailedAt,
                CompletedAt: null,
                BlockedBy: null,
                TransientRetry: transientRetry),

            CompletedIssue completed => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: completed.BranchName,
                PullRequestUrl: completed.PullRequestUrl,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: completed.CompletedAt,
                BlockedBy: null,
                TransientRetry: null),

            RevisionQueuedIssue revisionQueued => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: revisionQueued.BranchName,
                PullRequestUrl: revisionQueued.PullRequestUrl,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                TransientRetry: null),

            RevisionInProgressIssue revisionInProgress => new IssueStateDetails(
                WorkerRunId: revisionInProgress.WorkerRunId,
                BranchName: revisionInProgress.BranchName,
                PullRequestUrl: revisionInProgress.PullRequestUrl,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                TransientRetry: null),

            RevisionFailedIssue revisionFailed => new IssueStateDetails(
                WorkerRunId: revisionFailed.WorkerRunId,
                BranchName: revisionFailed.BranchName,
                PullRequestUrl: revisionFailed.PullRequestUrl,
                FeedbackCutoffAt: null,
                FailureReason: revisionFailed.FailureReason,
                FailedAt: revisionFailed.FailedAt,
                CompletedAt: null,
                BlockedBy: null,
                TransientRetry: null),

            ContinuableFailedIssue continuableFailed => new IssueStateDetails(
                WorkerRunId: continuableFailed.WorkerRunId,
                BranchName: continuableFailed.BranchName,
                PullRequestUrl: continuableFailed.PullRequestUrl.Length > 0 ? continuableFailed.PullRequestUrl : null,
                FeedbackCutoffAt: null,
                FailureReason: continuableFailed.FailureReason,
                FailedAt: continuableFailed.FailedAt,
                CompletedAt: null,
                BlockedBy: null,
                TransientRetry: transientRetry),

            ContinuationQueuedIssue continuationQueued => new IssueStateDetails(
                WorkerRunId: null,
                BranchName: continuationQueued.BranchName,
                PullRequestUrl: null,
                FeedbackCutoffAt: null,
                FailureReason: null,
                FailedAt: null,
                CompletedAt: null,
                BlockedBy: null,
                TransientRetry: null),

            _ => null
        };

    public async Task<IReadOnlyList<IssueSummary>> GetActiveIssueSummariesAsync(
        MonitoredRepositoryId? repositoryId,
        IReadOnlyCollection<string>? states,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<string> effectiveStates =
            states is null || states.Count == 0
                ? IssueStateRegistry.Active
                : states;

        Expression<Func<Issue, bool>> statePredicate = BuildTypeOrPredicate(effectiveStates);

        IQueryable<Issue> query = db.Set<Issue>()
            .AsNoTracking()
            .Where(statePredicate);

        if (repositoryId is not null)
        {
            query = query.Where(i => i.MonitoredRepositoryId == repositoryId);
        }

        // No SQL ordering: queued issues require in-memory DispatchOrderKey ordering,
        // and non-queued use DetectedAt DESC. Both partitions are ordered in-memory below.
        List<Issue> issues = await query
            .ToListAsync(cancellationToken);

        return await EnrichAndOrderActiveAsync(issues, cancellationToken);
    }

    private async Task<IReadOnlyList<IssueSummary>> EnrichAndOrderActiveAsync(
        List<Issue> issues,
        CancellationToken cancellationToken)
    {
        if (issues.Count == 0)
        {
            return [];
        }

        HashSet<MonitoredRepositoryId> repositoryIds = issues
            .Select(i => i.MonitoredRepositoryId)
            .ToHashSet();

        HashSet<Guid> repositoryGuids = repositoryIds
            .Select(id => id.Value)
            .ToHashSet();

        IReadOnlyDictionary<MonitoredRepositoryId, string> slugs = await slugQueries.GetSlugsAsync(
            repositoryIds,
            cancellationToken);

        IReadOnlyList<EligibleRepository> eligibleRepositories = await eligibilityQuery.GetEligibleRepositoriesAsync(
            repositoryGuids,
            cancellationToken);

        IReadOnlyDictionary<Guid, string> eligibilityStatuses = await eligibilityQuery.GetEligibilityStatusesAsync(
            repositoryGuids,
            cancellationToken);

        List<Guid> issueIds = issues
            .Select(i => i.Id.Value)
            .ToList();

        IReadOnlyDictionary<Guid, RunAggregate> runAggregates = await workerRunQueries.GetRunAggregatesForIssuesAsync(
            issueIds,
            cancellationToken);

        // Build a position lookup: repoId → Position for eligible repos only.
        Dictionary<Guid, int> positionByRepo = eligibleRepositories
            .ToDictionary(r => r.Id, r => r.Position);

        List<QueuedIssue> queuedIssues = issues
            .OfType<QueuedIssue>()
            .ToList();

        List<Issue> nonQueuedIssues = issues
            .Where(i => i is not QueuedIssue)
            .OrderByDescending(i => i.DetectedAt)
            .ToList();

        // Partition queued issues once — single TryGetValue lookup per issue avoids
        // ContainsKey + indexer double-lookup.
        // For ineligible issues (IsEligible = false), pos defaults to 0 here, but Position is
        // not consumed for them — ineligible issues pass int.MaxValue as the sentinel position
        // to DispatchOrderKey.For, ensuring they sort after all eligible queued issues.
        List<(QueuedIssue Issue, bool IsEligible, int Position)> queuedWithPosition = queuedIssues
            .Select(i => (
                Issue: i,
                IsEligible: positionByRepo.TryGetValue(i.MonitoredRepositoryId.Value, out int pos),
                Position: pos))
            .ToList();

        // Eligible-repo queued issues (real position from eligibility query).
        List<Issue> eligibleQueued = queuedWithPosition
            .Where(t => t.IsEligible)
            .OrderBy(t => DispatchOrderKey.For(t.Issue, t.Position))
            .Select(t => (Issue)t.Issue)
            .ToList();

        // Ineligible-repo queued issues: sentinel position so they sort among themselves
        // by DetectedAt then Id, consistently after all eligible queued issues.
        List<Issue> ineligibleQueued = queuedWithPosition
            .Where(t => !t.IsEligible)
            .OrderBy(t => DispatchOrderKey.For(t.Issue, int.MaxValue))
            .Select(t => (Issue)t.Issue)
            .ToList();

        List<Issue> orderedIssues = [..eligibleQueued, ..ineligibleQueued, ..nonQueuedIssues];

        return orderedIssues
            .Select(i => new IssueSummary(
                Id: i.Id.Value,
                IssueNumber: i.IssueNumber,
                Title: i.Title,
                State: GetStateDiscriminator(i),
                RepositorySlug: slugs.TryGetValue(i.MonitoredRepositoryId, out string? slug) ? slug : string.Empty,
                DetectedAt: i.DetectedAt,
                Url: i.Url.Value.ToString(),
                FailureClassification: GetFailureCategory(i),
                RepositoryEligibilityStatus: eligibilityStatuses.TryGetValue(i.MonitoredRepositoryId.Value, out string? status)
                    ? status
                    : null,
                RunStats: runAggregates.TryGetValue(i.Id.Value, out RunAggregate? aggregate)
                    ? MapRunStats(aggregate)
                    : null))
            .ToList();
    }

    private static Expression<Func<Issue, bool>> BuildTypeOrPredicate(IReadOnlyCollection<string> stateNames)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(Issue), "i");

        Expression? body = null;
        foreach (string name in stateNames)
        {
            Type? entityType = IssueStateRegistry.GetEntityType(name);
            if (entityType is null)
            {
                continue;
            }

            // Expression.TypeIs over the TPH hierarchy is translated by EF Core to a
            // `state = '<discriminator>'` SQL filter — this is server-side, not client evaluation.
            Expression typeCheck = Expression.TypeIs(parameter, entityType);
            body = body is null ? typeCheck : Expression.OrElse(body, typeCheck);
        }

        // When no recognised names map to a type (should not happen with validated input),
        // return a predicate that matches nothing.
        body ??= Expression.Constant(false);

        return Expression.Lambda<Func<Issue, bool>>(body, parameter);
    }

    /// <summary>
    /// Returns a page of resolved issue summaries in DetectedAt DESC, Id ASC order.
    ///
    /// Cursor contract: the caller is responsible for validating the cursor via
    /// <see cref="IssueCursor.Decode"/> before calling this method. When <paramref name="cursor"/>
    /// is non-null this method assumes it is well-formed. The endpoint (step 6) validates
    /// the cursor and returns 400 on <see cref="IssueErrors.InvalidCursor"/> before calling here.
    /// </summary>
    public async Task<PagedIssues> GetResolvedIssueSummariesAsync(
        MonitoredRepositoryId? repositoryId,
        IReadOnlyCollection<string> states,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        const int DefaultLimit = 50;
        const int MaxLimit = 100;

        int effectiveLimit = limit <= 0
            ? DefaultLimit
            : Math.Min(limit, MaxLimit);

        Expression<Func<Issue, bool>> statePredicate = BuildTypeOrPredicate(states);

        IQueryable<Issue> query = db.Set<Issue>()
            .AsNoTracking()
            .Where(statePredicate);

        if (repositoryId is not null)
        {
            query = query.Where(i => i.MonitoredRepositoryId == repositoryId);
        }

        if (cursor is not null)
        {
            Result<(DateTimeOffset DetectedAt, IssueId Id)> decoded = IssueCursor.Decode(cursor);
            if (decoded is Result<(DateTimeOffset DetectedAt, IssueId Id)>.Success success)
            {
                DateTimeOffset cursorDetectedAt = success.Value.DetectedAt;
                IssueId cursorId = success.Value.Id;

                query = query.Where(i =>
                    i.DetectedAt < cursorDetectedAt
                    || (i.DetectedAt == cursorDetectedAt && i.Id > cursorId));
            }
        }

        List<Issue> issues = await query
            .OrderByDescending(i => i.DetectedAt)
            .ThenBy(i => i.Id)
            .Take(effectiveLimit + 1)
            .ToListAsync(cancellationToken);

        bool hasNextPage = issues.Count > effectiveLimit;
        List<Issue> pageIssues = hasNextPage
            ? issues.Take(effectiveLimit).ToList()
            : issues;

        IReadOnlyList<IssueSummary> summaries = await EnrichAsync(pageIssues, cancellationToken);

        string? nextCursor = null;
        if (hasNextPage && summaries.Count > 0)
        {
            Issue lastIssue = pageIssues[^1];
            nextCursor = IssueCursor.Encode(lastIssue.DetectedAt, lastIssue.Id);
        }

        return new PagedIssues(summaries, nextCursor);
    }

    public async Task<IssueStateCounts> GetIssueStateCountsAsync(
        MonitoredRepositoryId? repositoryId,
        CancellationToken cancellationToken)
    {
        IQueryable<Issue> query = db.Set<Issue>()
            .AsNoTracking();

        if (repositoryId is not null)
        {
            query = query.Where(i => i.MonitoredRepositoryId == repositoryId);
        }

        List<StateCountRow> rows = await query
            .GroupBy(i => EF.Property<string>(i, "state"))
            .Select(g => new StateCountRow(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        Dictionary<string, int> counts = IssueStateRegistry.Active
            .Concat(IssueStateRegistry.Resolved)
            .ToDictionary(name => name, _ => 0, StringComparer.Ordinal);

        foreach (StateCountRow row in rows)
        {
            // Skip discriminator values not in the registry — TryGetValue avoids a double lookup.
            if (counts.TryGetValue(row.State, out _))
            {
                counts[row.State] = row.Count;
            }
        }

        return new IssueStateCounts(counts);
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

    private sealed record StateCountRow(string State, int Count);
}
