using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
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

    private AwaitingInputIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    // Placeholder replaced by the factory method or EF materialisation; invalid until assigned.
    public ResumeTarget ResumeTarget { get; private set; } = new ResumeTarget.Fresh();

    public QuestionPost QuestionPost { get; private set; } = new QuestionPost.Pending();

    /// <summary>The round's one reminder, posted in the question's thread once the question has waited without input.</summary>
    public QuestionReminder Reminder { get; private set; } = new QuestionReminder.Unsent();

    public override string? LinkedPullRequestUrl =>
        ResumeTarget is ResumeTarget.Revision revision ? revision.PullRequestUrl : null;

    /// <summary>
    /// The branch the work resumes on, whose head counts as input while the issue waits: set for a revision or a
    /// continuation, <see langword="null"/> when nothing was pushed or triage asked.
    /// </summary>
    public string? ResumeBranchName => ResumeTarget switch
    {
        ResumeTarget.Revision revision => revision.BranchName,
        ResumeTarget.Continuation continuation => continuation.BranchName,
        _ => null,
    };

    // The stage is the one the answer resumes: a re-triage waits in Triage, every other target in Implement.
    public override IssueStage Stage =>
        ResumeTarget is ResumeTarget.Retriage ? IssueStage.Triage : IssueStage.Implement;

    public override bool NeedsAction => true;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    // The step is the one the answer resumes, as the stage is.
    public override string? TransitionCause => null;

    public override IssueLifecycleStep LifecycleStep =>
        ResumeTarget is ResumeTarget.Retriage ? IssueLifecycleStep.Triage : IssueLifecycleStep.Implement;

    public override RunKind? RunKind => null;

    /// <summary>The questions waiting for input: the open round of the clarification log.</summary>
    public IReadOnlyList<InputQuestion> Questions => OpenClarification?.Questions ?? [];

    internal static AwaitingInputIssue FromInProgress(
        InProgressIssue source,
        ResumeTarget resumeTarget,
        IReadOnlyList<InputQuestion> questions,
        DateTimeOffset askedAt,
        DateTimeOffset transitionedAt)
    {
        AwaitingInputIssue awaiting = new(source.Id, transitionedAt);
        awaiting.CopySharedPropertiesFrom(source);
        awaiting.Enter(source.WorkerRunId, resumeTarget, questions, decisions: [], askedAt);
        return awaiting;
    }

    internal static AwaitingInputIssue FromRevisionInProgress(
        RevisionInProgressIssue source,
        IReadOnlyList<InputQuestion> questions,
        DateTimeOffset askedAt,
        DateTimeOffset transitionedAt)
    {
        AwaitingInputIssue awaiting = new(source.Id, transitionedAt);
        awaiting.CopySharedPropertiesFrom(source);
        awaiting.Enter(
            source.WorkerRunId,
            new ResumeTarget.Revision(source.BranchName, source.PullRequestUrl),
            questions,
            decisions: [],
            askedAt);
        return awaiting;
    }

    /// <summary>
    /// Triage needs answers before the first implement run (ADR 0097): the verdict is recorded, its questions open a
    /// clarification round, and input sends the issue back to triage.
    /// </summary>
    internal static AwaitingInputIssue FromTriageInProgress(
        TriageInProgressIssue source,
        TriageVerdict.NeedsInput verdict,
        IReadOnlyList<TriageDecision> decisions,
        DateTimeOffset transitionedAt)
    {
        AwaitingInputIssue awaiting = new(source.Id, transitionedAt);
        awaiting.CopySharedPropertiesFrom(source);
        awaiting.SetBlockedBy(source.BlockedBy);
        awaiting.RecordTriageVerdict(verdict);
        awaiting.Enter(source.WorkerRunId, new ResumeTarget.Retriage(), verdict.Questions, decisions, verdict.DecidedAt);
        return awaiting;
    }

    private void Enter(
        WorkerRunId workerRunId,
        ResumeTarget resumeTarget,
        IReadOnlyList<InputQuestion> questions,
        IReadOnlyList<TriageDecision> decisions,
        DateTimeOffset askedAt)
    {
        WorkerRunId = workerRunId;
        ResumeTarget = resumeTarget;
        QuestionPost = new QuestionPost.Pending();
        Reminder = new QuestionReminder.Unsent();
        AskClarification(questions, decisions, askedAt);
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
    /// Retry post: an operator sends a failed question post back to <see cref="QuestionPost.Pending"/>, so the post
    /// step runs again at once, whether the failure was permanent or is waiting on its backoff. Returns
    /// <see langword="false"/> and changes nothing unless the post failed.
    /// </summary>
    public bool RetryQuestionPost()
    {
        if (QuestionPost is not QuestionPost.Failed)
        {
            return false;
        }

        QuestionPost = new QuestionPost.Pending();
        return true;
    }

    /// <summary>Records the posted reminder; a reminder already sent keeps its first comment.</summary>
    public void RecordReminderPosted(long commentId, DateTimeOffset sentAt)
    {
        if (Reminder is QuestionReminder.Sent)
        {
            return;
        }

        Reminder = new QuestionReminder.Sent(commentId, sentAt);
    }

    /// <summary>Records a failed reminder post; a reminder already sent ignores a late failure.</summary>
    public void RecordReminderPostFailed(string reason, bool transient, DateTimeOffset now)
    {
        if (Reminder is QuestionReminder.Sent)
        {
            return;
        }

        Reminder = Reminder.Fail(reason, transient, now);
    }

    /// <summary>
    /// Whether input that arrived at <paramref name="arrivedAt"/> answers the open round: only once the question is
    /// posted, and only input after its post time, since anything earlier belongs to an earlier round.
    /// </summary>
    public bool AcceptsInputArrivedAt(DateTimeOffset arrivedAt) =>
        QuestionPost is QuestionPost.Posted posted && arrivedAt > posted.PostedAt;

    /// <summary>
    /// Closes the open clarification round with <paramref name="input"/> and resumes along the
    /// <see cref="ResumeTarget"/>. The input must be one <see cref="AcceptsInputArrivedAt"/> accepts.
    /// </summary>
    public QueuedIssue ProvideInput(ClarificationInput input, DateTimeOffset answeredAt, DateTimeOffset transitionedAt)
    {
        if (!AcceptsInputArrivedAt(answeredAt))
        {
            throw new InvalidOperationException("Input is only read once the question is posted, and only after its post time.");
        }

        return ResumeTarget is ResumeTarget.Retriage
            ? Retriage(input, answeredAt, transitionedAt)
            : Resume(input, answeredAt, transitionedAt);
    }

    /// <summary>
    /// Continue without answer: an operator closes the open round as unanswered, recording a
    /// <see cref="InputPart.ContinuedWithoutAnswer"/> part at <paramref name="now"/>, whatever the post status. The work
    /// resumes along the <see cref="ResumeTarget"/>, and the resumed run refuses a <c>needs_input</c> verdict. A
    /// re-triage target goes through the dependency check as Skip triage does instead: a triage run that can't get its
    /// answer would only ask again.
    /// </summary>
    public Issue ContinueWithoutAnswer(DateTimeOffset now)
    {
        ClarificationInput unanswered = ClarificationInput.ContinuedWithoutAnswer();

        if (ResumeTarget is ResumeTarget.Retriage)
        {
            AnswerClarification(unanswered, now);
            return ReleaseThroughDependencyCheck(verdict: null, now);
        }

        return Resume(unanswered, now, now);
    }

    private TriageQueuedIssue Retriage(ClarificationInput input, DateTimeOffset answeredAt, DateTimeOffset transitionedAt)
    {
        TriageQueuedIssue triageQueued = TriageQueuedIssue.FromAwaitingInput(this, input, answeredAt, transitionedAt);
        AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
        return triageQueued;
    }

    private QueuedIssue Resume(ClarificationInput input, DateTimeOffset answeredAt, DateTimeOffset transitionedAt)
    {
        switch (ResumeTarget)
        {
            case ResumeTarget.Revision revision:
                RevisionQueuedIssue revisionQueued = RevisionQueuedIssue.FromAwaitingInput(
                    this,
                    revision,
                    input,
                    answeredAt,
                    transitionedAt);
                AddDomainEvent(new Events.IssueRevisionQueued(Id, MonitoredRepositoryId));
                return revisionQueued;

            case ResumeTarget.Continuation continuation:
                ContinuationQueuedIssue continuationQueued = ContinuationQueuedIssue.FromAwaitingInput(
                    this,
                    continuation,
                    input,
                    answeredAt,
                    transitionedAt);
                AddDomainEvent(new Events.IssueContinuationQueued(Id, MonitoredRepositoryId));
                return continuationQueued;

            default:
                FreshQueuedIssue freshQueued = FreshQueuedIssue.FromAwaitingInput(this, input, answeredAt, transitionedAt);
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
