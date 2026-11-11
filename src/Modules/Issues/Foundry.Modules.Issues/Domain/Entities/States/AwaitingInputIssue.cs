using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// An issue whose run stopped to ask a person one or more questions (ADR 0096). Its questions are the open round on
/// the clarification log; it waits for input on the thread they are posted to, then resumes along its
/// <see cref="ResumeTarget"/>.
/// </summary>
public sealed class AwaitingInputIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private AwaitingInputIssue()
    {
    }

    private AwaitingInputIssue(IssueId id) : base(id)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    // Placeholder replaced by the factory method or EF materialisation; invalid until assigned.
    public ResumeTarget ResumeTarget { get; private set; } = new ResumeTarget.Fresh();

    public QuestionPost QuestionPost { get; private set; } = new QuestionPost.Pending();

    public override string? LinkedPullRequestUrl =>
        ResumeTarget is ResumeTarget.Revision revision ? revision.PullRequestUrl : null;

    /// <summary>The questions waiting for input: the open round of the clarification log.</summary>
    public IReadOnlyList<InputQuestion> Questions => OpenClarification?.Questions ?? [];

    internal static AwaitingInputIssue FromInProgress(
        InProgressIssue source,
        ResumeTarget resumeTarget,
        IReadOnlyList<InputQuestion> questions,
        DateTimeOffset askedAt)
    {
        AwaitingInputIssue awaiting = new(source.Id);
        awaiting.CopySharedPropertiesFrom(source);
        awaiting.Enter(source.WorkerRunId, resumeTarget, questions, askedAt);
        return awaiting;
    }

    internal static AwaitingInputIssue FromRevisionInProgress(
        RevisionInProgressIssue source,
        IReadOnlyList<InputQuestion> questions,
        DateTimeOffset askedAt)
    {
        AwaitingInputIssue awaiting = new(source.Id);
        awaiting.CopySharedPropertiesFrom(source);
        awaiting.Enter(
            source.WorkerRunId,
            new ResumeTarget.Revision(source.BranchName, source.PullRequestUrl),
            questions,
            askedAt);
        return awaiting;
    }

    /// <summary>
    /// Triage needs answers before the first implement run (ADR 0097): the verdict is recorded, its questions open a
    /// clarification round, and input sends the issue back to triage.
    /// </summary>
    internal static AwaitingInputIssue FromTriageInProgress(TriageInProgressIssue source, TriageVerdict.NeedsInput verdict)
    {
        AwaitingInputIssue awaiting = new(source.Id);
        awaiting.CopySharedPropertiesFrom(source);
        awaiting.SetBlockedBy(source.BlockedBy);
        awaiting.RecordTriageVerdict(verdict);
        awaiting.Enter(source.WorkerRunId, new ResumeTarget.Retriage(), verdict.Questions, verdict.DecidedAt);
        return awaiting;
    }

    private void Enter(
        WorkerRunId workerRunId,
        ResumeTarget resumeTarget,
        IReadOnlyList<InputQuestion> questions,
        DateTimeOffset askedAt)
    {
        WorkerRunId = workerRunId;
        ResumeTarget = resumeTarget;
        QuestionPost = new QuestionPost.Pending();
        AskClarification(questions, askedAt);
    }

    /// <summary>Records the posted question comment; a question already posted keeps its first comment.</summary>
    public void RecordQuestionPosted(long commentId, DateTimeOffset postedAt, string? commentUrl)
    {
        if (QuestionPost is QuestionPost.Posted)
        {
            return;
        }

        QuestionPost = new QuestionPost.Posted(commentId, postedAt, commentUrl);
    }

    /// <summary>Records a failed post; a question already posted ignores a late failure.</summary>
    public void RecordQuestionPostFailed(string reason, bool transient, DateTimeOffset now)
    {
        if (QuestionPost is QuestionPost.Posted)
        {
            return;
        }

        QuestionPost = QuestionPost.Fail(reason, transient, now);
    }

    /// <summary>
    /// Closes the open clarification round with <paramref name="input"/> and resumes along the
    /// <see cref="ResumeTarget"/>. Input is only read once the question is posted.
    /// </summary>
    public QueuedIssue ProvideInput(string input, DateTimeOffset answeredAt)
    {
        if (QuestionPost is not QuestionPost.Posted)
        {
            throw new InvalidOperationException("Input is only read once the question is posted.");
        }

        switch (ResumeTarget)
        {
            case ResumeTarget.Revision revision:
                RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromAwaitingInput(this, revision, input, answeredAt);
                AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
                return revisionQueued;

            case ResumeTarget.Continuation continuation:
                ContinuationQueuedIssue continuationQueued = ContinuationQueuedIssue.FromAwaitingInput(
                    this,
                    continuation,
                    input,
                    answeredAt);
                AddDomainEvent(new Events.IssueContinuationQueued(Id, MonitoredRepositoryId));
                return continuationQueued;

            case ResumeTarget.Retriage:
                TriageQueuedIssue triageQueued = TriageQueuedIssue.FromAwaitingInput(this, input, answeredAt);
                AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
                return triageQueued;

            default:
                FreshQueuedIssue freshQueued = FreshQueuedIssue.FromAwaitingInput(this, input, answeredAt);
                AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
                return freshQueued;
        }
    }

    /// <summary>The provider issue closed while waiting: the issue completes, as from review.</summary>
    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromAwaitingInput(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    /// <summary>The pull request closed without merging while waiting: the branch stays continuable, as from review.</summary>
    public ContinuableFailedIssue Fail(string failureReason, FailureCategory failureCategory, DateTimeOffset failedAt)
    {
        if (ResumeTarget is not ResumeTarget.Revision revision)
        {
            throw new InvalidOperationException("Only an issue waiting on a pull request fails when it closes.");
        }

        ContinuableFailedIssue failed = ContinuableFailedIssue.FromAwaitingInput(
            this,
            revision,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
