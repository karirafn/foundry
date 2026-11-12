using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// Builds Issue lifecycle states via the real production transition path
/// (DetectedIssue.Detect → QueueTriage → … → Claim → …). Intent-named terminals
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
    private KindClassification _kindClassification = KindClassification.Unclassified;
    private IReadOnlyList<int> _blockedBy = [];

    private WorkerRunId _workerRunId = WorkerRunId.New();
    private string _branchName = "feat/1-test";
    private string _pullRequestUrl = "https://github.com/owner/repo/pull/1";
    private DateTimeOffset _feedbackCutoffAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _enteredChecksPendingAt = DateTimeOffset.UtcNow;
    private ChecksOutcome _checksOutcome = new ChecksOutcome.Passed();
    private RebaseOutcome _rebaseOutcome = new RebaseOutcome.UpToDate();
    private DateTimeOffset _completedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _failedAt = DateTimeOffset.UtcNow;
    private string _failureReason = "Container exited with code 1";
    private FailureCategory _failureCategory = FailureCategory.NonZeroExit;
    private IReadOnlyList<ReviewComment> _reviewComments = [new ReviewComment("Please fix.")];
    private int _omittedCommentCount;
    private DateTimeOffset? _newestCommentAt;
    private DateTimeOffset _runStartedAt = DateTimeOffset.UtcNow;
    private IReadOnlyList<FailingCheck> _failingChecks =
    [
        new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1"),
    ];

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

    public IssueBuilder WithKindClassification(KindClassification value) { _kindClassification = value; return this; }

    public IssueBuilder WithWorkerRunId(WorkerRunId value) { _workerRunId = value; return this; }

    public IssueBuilder WithBranchName(string value) { _branchName = value; return this; }

    public IssueBuilder WithPullRequestUrl(string value) { _pullRequestUrl = value; return this; }

    public IssueBuilder WithFeedbackCutoffAt(DateTimeOffset value) { _feedbackCutoffAt = value; return this; }

    public IssueBuilder WithEnteredChecksPendingAt(DateTimeOffset value) { _enteredChecksPendingAt = value; return this; }

    public IssueBuilder WithChecksOutcome(ChecksOutcome value) { _checksOutcome = value; return this; }

    public IssueBuilder WithRebaseOutcome(RebaseOutcome value) { _rebaseOutcome = value; return this; }

    public IssueBuilder WithCompletedAt(DateTimeOffset value) { _completedAt = value; return this; }

    public IssueBuilder WithFailedAt(DateTimeOffset value) { _failedAt = value; return this; }

    public IssueBuilder WithFailureReason(string value) { _failureReason = value; return this; }

    public IssueBuilder WithFailureCategory(FailureCategory value) { _failureCategory = value; return this; }

    public IssueBuilder WithReviewComments(IEnumerable<ReviewComment> value) { _reviewComments = [.. value]; return this; }

    public IssueBuilder WithOmittedCommentCount(int value) { _omittedCommentCount = value; return this; }

    public IssueBuilder WithNewestCommentAt(DateTimeOffset value) { _newestCommentAt = value; return this; }

    public IssueBuilder WithRunStartedAt(DateTimeOffset value) { _runStartedAt = value; return this; }

    public IssueBuilder WithFailingChecks(IEnumerable<FailingCheck> value) { _failingChecks = [.. value]; return this; }

    /// <summary>Records blockers on the issue from detection, which every later state carries forward.</summary>
    public IssueBuilder WithBlockedBy(IEnumerable<int> value) { _blockedBy = [.. value]; return this; }

    public DetectedIssue Detected()
    {
        DetectedIssue detected = DetectedIssue.Detect(
            _monitoredRepositoryId,
            _issueNumber,
            _title,
            _author,
            _url,
            _labels,
            _detectedAt,
            _kindClassification);

        if (_blockedBy.Count > 0)
        {
            detected.SetBlockedBy(_blockedBy);
        }

        return detected;
    }

    public TriageQueuedIssue TriageQueued() => Detected().QueueTriage();

    /// <summary>A new issue waiting untriaged for its <paramref name="blockers"/> to close (ADR 0102).</summary>
    public TriageBlockedIssue TriageBlocked(IReadOnlyList<int> blockers) => Detected().BlockTriage(blockers);

    public TriageInProgressIssue TriageInProgress() => TriageQueued().Claim(_workerRunId);

    public TriageFailedIssue TriageFailed() =>
        TriageInProgress().MarkFailed(_failureReason, _failureCategory, _failedAt);

    /// <summary>A queued issue that skipped triage: no verdict recorded, its kind as the labels decided.</summary>
    public FreshQueuedIssue FreshQueued() => (FreshQueuedIssue)TriageFailed().SkipTriage();

    /// <summary>A queued issue triage judged ready as <paramref name="kind"/>.</summary>
    public FreshQueuedIssue FreshQueuedFromTriage(IssueKind kind) =>
        (FreshQueuedIssue)TriageInProgress().MarkReady(new TriageVerdict.Ready(kind, _runStartedAt));

    /// <summary>A triage run that asked the <see cref="DefaultQuestions"/>: resumes as a re-triage.</summary>
    public AwaitingInputIssue AwaitingInputFromTriage(IssueKind kind) =>
        TriageInProgress().MarkNeedsInput(new TriageVerdict.NeedsInput(kind, DefaultQuestions, _runStartedAt));

    /// <summary>A triage run that named the blockers of <paramref name="verdict"/>: waits untriaged on them.</summary>
    public TriageBlockedIssue TriageBlockedFromTriage(TriageVerdict.Blocked verdict) =>
        TriageInProgress().MarkBlocked(verdict);

    public InProgressIssue InProgress() => FreshQueued().Claim(_workerRunId);

    public ReviewIssue Review() =>
        InProgress().MarkInReview(_branchName, _pullRequestUrl, _feedbackCutoffAt);

    public RevisionQueuedIssue RevisionQueued() =>
        Review().Revise(_reviewComments, _omittedCommentCount, _newestCommentAt);

    public RevisionInProgressIssue RevisionInProgress() => RevisionQueued().Claim(_workerRunId);

    public FailedIssue Failed() =>
        InProgress().MarkFailed(_failureReason, _failedAt, _failureCategory);

    public ContinuableFailedIssue ContinuableFailed() =>
        InProgress().MarkContinuableFailed(_branchName, _failureReason, _failureCategory, _failedAt);

    public ContinuableFailedIssue ContinuableFailedFromReview() =>
        Review().Fail(_failureReason, _failureCategory, _failedAt);

    public RevisionFailedIssue RevisionFailed() =>
        RevisionInProgress().MarkFailed(_failureReason, _failureCategory, _failedAt);

    /// <summary>An implement run whose publish retries ran out (ADR 0100).</summary>
    public PublishFailedIssue PublishFailed() =>
        InProgress().MarkPublishFailed(_branchName, _failureReason, _failedAt);

    /// <summary>A revision run whose publish retries ran out (ADR 0100).</summary>
    public PublishFailedIssue PublishFailedFromRevision() =>
        RevisionInProgress().MarkPublishFailed(_failureReason, _failedAt);

    /// <summary>An implement run whose publish the Diff gate blocked (#788).</summary>
    public PublishBlockedIssue PublishBlocked() =>
        InProgress().MarkPublishBlocked(_branchName, _failedAt);

    /// <summary>A revision run whose publish the Diff gate blocked (#788).</summary>
    public PublishBlockedIssue PublishBlockedFromRevision() =>
        RevisionInProgress().MarkPublishBlocked(_failedAt);

    /// <summary>An implement run whose work went to <paramref name="sideBranchName"/> because merging it conflicted.</summary>
    public BranchDivergedIssue BranchDiverged(string sideBranchName = "feat/1-test-run-1") =>
        InProgress().MarkBranchDiverged(_branchName, sideBranchName, pullRequestUrl: null);

    /// <summary>A revision run whose work went to <paramref name="sideBranchName"/> because merging it conflicted.</summary>
    public BranchDivergedIssue BranchDivergedFromRevision(string sideBranchName = "feat/1-test-run-1") =>
        RevisionInProgress().MarkBranchDiverged(sideBranchName);

    public RebasePendingIssue RebasePending() =>
        InProgress().MarkRebasePending(_branchName, _pullRequestUrl, _feedbackCutoffAt);

    public RebasePendingIssue RebasePendingFromRevision() => RevisionInProgress().MarkRebasePending(_runStartedAt);

    /// <summary>
    /// Builds a <see cref="RebasePendingIssue"/> that has already dispatched <paramref name="conflicts"/>
    /// consecutive conflict-resolution revisions (RebaseConflictAttempts = <paramref name="conflicts"/>).
    /// </summary>
    public RebasePendingIssue RebasePendingAfterConflicts(int conflicts)
    {
        RebasePendingIssue rebasePending = RebasePending();
        for (int i = 0; i < conflicts; i++)
        {
            rebasePending = rebasePending.ResolveConflict().Claim(_workerRunId).MarkRebasePending(_runStartedAt);
        }

        return rebasePending;
    }

    public ChecksPendingIssue ChecksPending() => RebasePending().MarkChecksPending(_rebaseOutcome);

    public ChecksPendingIssue ChecksPendingFromRevision() => RebasePendingFromRevision().MarkChecksPending(_rebaseOutcome);

    /// <summary>
    /// Builds a <see cref="ChecksPendingIssue"/> that arrived via a FixChecks revision dispatch
    /// (i.e., ChecksFixAttempts = 1). Used to test the second-attempt code path in FixChecks scenarios.
    /// </summary>
    public ChecksPendingIssue ChecksPendingFromFixChecks() =>
        ChecksPending().FixChecks(_failingChecks).Claim(_workerRunId).MarkRebasePending(_runStartedAt).MarkChecksPending(_rebaseOutcome);

    /// <summary>
    /// Builds a <see cref="ChecksPendingIssue"/> that has exhausted all FixChecks dispatch
    /// attempts (ChecksFixAttempts = <see cref="ChecksPendingIssue.ChecksMaxFixPushes"/> = 2).
    /// At the cap, a <c>still_failing</c> verdict transitions to <see cref="ReviewIssue"/> rather
    /// than dispatching another fix revision.
    /// </summary>
    public ChecksPendingIssue ChecksPendingAtCap() =>
        ChecksPendingFromFixChecks().FixChecks(_failingChecks).Claim(_workerRunId).MarkRebasePending(_runStartedAt).MarkChecksPending(_rebaseOutcome);

    public ReviewIssue ReviewFromChecksPending() => ChecksPending().ToReview(_checksOutcome);

    public UnchangedIssue Unchanged() => InProgress().MarkUnchanged();

    public CompletedIssue Completed() => Review().Complete(_completedAt);

    /// <summary>The default questions an awaiting-input terminal asks.</summary>
    public static IReadOnlyList<InputQuestion> DefaultQuestions { get; } =
        [new InputQuestion("Which database?", "Two are configured.", "Postgres.")];

    /// <summary>An implement run that opened a pull request and then asked: resumes as a revision.</summary>
    public AwaitingInputIssue AwaitingInput() =>
        InProgress().MarkAwaitingInput(_branchName, _pullRequestUrl, branchHasCommits: true, DefaultQuestions, _runStartedAt);

    /// <summary>An implement run that asked without opening a pull request: resumes as a continuation or fresh.</summary>
    public AwaitingInputIssue AwaitingInputWithoutPullRequest(bool branchHasCommits) =>
        InProgress().MarkAwaitingInput(_branchName, pullRequestUrl: null, branchHasCommits, DefaultQuestions, _runStartedAt);

    /// <summary>An awaiting-input issue whose questions were posted at <paramref name="postedAt"/>.</summary>
    public AwaitingInputIssue AwaitingInputPosted(DateTimeOffset postedAt, bool withPullRequest = true)
    {
        AwaitingInputIssue awaiting = withPullRequest ? AwaitingInput() : AwaitingInputWithoutPullRequest(branchHasCommits: true);
        awaiting.RecordQuestionPosted(commentId: 1001, postedAt, commentUrl: null);
        return awaiting;
    }
}
