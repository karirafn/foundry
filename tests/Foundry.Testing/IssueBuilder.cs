using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Testing;

/// <summary>
/// Builds Issue lifecycle states via the real production transition path
/// (DetectedIssue.Detect → Enqueue → Claim → …). Intent-named terminals
/// replay the domain transitions rather than stamping properties directly.
///
/// Domain-events note: each production transition raises its domain event on the
/// <em>source</em> aggregate (the object the method is called on), not on the
/// returned aggregate. Every terminal therefore yields a fresh aggregate with an
/// empty domain-events list, so tests can call one more transition and assert
/// ShouldHaveSingleItem() without noise from the construction chain.
/// </summary>
public sealed class IssueBuilder
{
    private MonitoredRepositoryId _monitoredRepositoryId = MonitoredRepositoryId.New();
    private int _issueNumber = 1;
    private string _title = "Test Issue";
    private IssueAuthor _author = IssueAuthor.Create("octocat").ValueOrThrow();
    private ProviderUrl _url = ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();
    private IReadOnlyList<string> _labels = ["foundry"];
    private DateTimeOffset _detectedAt = DateTimeOffset.UtcNow;
    private IssueKind _issueKind = IssueKind.Feature;

    private WorkerRunId _workerRunId = WorkerRunId.New();
    private string _branchName = "feat/1-test";
    private string _pullRequestUrl = "https://github.com/owner/repo/pull/1";
    private DateTimeOffset _feedbackCutoffAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _completedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _failedAt = DateTimeOffset.UtcNow;
    private string _failureReason = "Container exited with code 1";
    private FailureCategory _failureCategory = FailureCategory.NonZeroExit;
    private IReadOnlyList<ReviewComment> _reviewComments = [new ReviewComment("Please fix.")];

    /// <summary>
    /// Exposes the configured worker run id so assertions can anchor to the aggregate's id. (AC #3)
    /// </summary>
    public WorkerRunId WorkerRunId => _workerRunId;

    public IssueBuilder WithMonitoredRepositoryId(MonitoredRepositoryId value) { _monitoredRepositoryId = value; return this; }

    public IssueBuilder WithIssueNumber(int value) { _issueNumber = value; return this; }

    public IssueBuilder WithTitle(string value) { _title = value; return this; }

    public IssueBuilder WithAuthor(IssueAuthor value) { _author = value; return this; }

    public IssueBuilder WithUrl(ProviderUrl value) { _url = value; return this; }

    public IssueBuilder WithLabels(IEnumerable<string> value) { _labels = [.. value]; return this; }

    public IssueBuilder WithDetectedAt(DateTimeOffset value) { _detectedAt = value; return this; }

    /// <remarks>
    /// Only affects <see cref="Detected"/>. Production sets <c>IssueKind</c> at detection
    /// alone (<c>SetSharedProperties</c> does not carry it), so terminals beyond
    /// <see cref="Detected"/> reset it to <see cref="IssueKind.Feature"/>.
    /// </remarks>
    public IssueBuilder WithIssueKind(IssueKind value) { _issueKind = value; return this; }

    public IssueBuilder WithWorkerRunId(WorkerRunId value) { _workerRunId = value; return this; }

    public IssueBuilder WithBranchName(string value) { _branchName = value; return this; }

    public IssueBuilder WithPullRequestUrl(string value) { _pullRequestUrl = value; return this; }

    public IssueBuilder WithFeedbackCutoffAt(DateTimeOffset value) { _feedbackCutoffAt = value; return this; }

    public IssueBuilder WithCompletedAt(DateTimeOffset value) { _completedAt = value; return this; }

    public IssueBuilder WithFailedAt(DateTimeOffset value) { _failedAt = value; return this; }

    public IssueBuilder WithFailureReason(string value) { _failureReason = value; return this; }

    public IssueBuilder WithFailureCategory(FailureCategory value) { _failureCategory = value; return this; }

    public IssueBuilder WithReviewComments(IEnumerable<ReviewComment> value) { _reviewComments = [.. value]; return this; }

    public DetectedIssue Detected() =>
        DetectedIssue.Detect(
            _monitoredRepositoryId,
            _issueNumber,
            _title,
            _author,
            _url,
            _labels,
            _detectedAt,
            _issueKind);

    public FreshQueuedIssue FreshQueued() => Detected().Enqueue();

    public InProgressIssue InProgress() => FreshQueued().Claim(_workerRunId);

    public ReviewIssue Review() =>
        InProgress().MarkInReview(_branchName, _pullRequestUrl, _feedbackCutoffAt);

    public RevisionQueuedIssue RevisionQueued() => Review().Revise(_reviewComments);

    public RevisionInProgressIssue RevisionInProgress() => RevisionQueued().Claim(_workerRunId);

    public FailedIssue Failed() =>
        InProgress().MarkFailed(_failureReason, _failedAt, _failureCategory);

    public ContinuableFailedIssue ContinuableFailed() =>
        InProgress().MarkContinuableFailed(_branchName, _failureReason, _failureCategory, _failedAt);

    public ContinuableFailedIssue ContinuableFailedFromReview() =>
        Review().Fail(_failureReason, _failureCategory, _failedAt);

    public RevisionFailedIssue RevisionFailed() =>
        RevisionInProgress().MarkFailed(_failureReason, _failureCategory, _failedAt);

    public UnchangedIssue Unchanged() => InProgress().MarkUnchanged();

    public CompletedIssue Completed() => Review().Complete(_completedAt);
}
