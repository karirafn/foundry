using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Entities;

public abstract class Issue : AggregateRoot<IssueId>, IStateMachine<Issue>
{
    // Private parameterless constructor for EF Core materialization.
    private protected Issue() : base(IssueId.New())
    {
    }

    /// <param name="id">The issue's id, kept across every state.</param>
    /// <param name="enteredStateAt">
    /// When the issue entered this state. Required so that no transition can leave it unstamped: the generic
    /// transition has no clock, so each state's factory receives the time from its caller.
    /// </param>
    protected Issue(IssueId id, DateTimeOffset enteredStateAt) : base(id)
    {
        if (enteredStateAt == DateTimeOffset.MinValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(enteredStateAt),
                "An issue state needs the time it was entered; the default value is not a time.");
        }

        EnteredStateAt = enteredStateAt;
    }

    // The persisted label half of KindClassification: the labelled kind, or null when no label decided it. The
    // triage half is the latest TriageVerdict's kind.
    private IssueKind? _labelledKind;

    /// <summary>
    /// How the issue's kind was decided: a kind label beats the latest triage verdict's kind, and neither is
    /// unclassified (ADR 0097). A label change re-derives the label half only, so removing the last kind label from a
    /// triaged issue leaves it triaged.
    /// </summary>
    public KindClassification KindClassification => KindClassification.From(_labelledKind, TriageVerdict?.Kind);

    /// <summary>
    /// The latest verdict a triage run returned (ADR 0097), or <see langword="null"/> when the issue was never triaged.
    /// Carried through every transition with the shared properties.
    /// </summary>
    public TriageVerdict? TriageVerdict { get; private set; }

    /// <summary>
    /// The kind triage judged when a label decided a different one, or <see langword="null"/>. Recorded only: the label
    /// decides, and nothing acts on the difference.
    /// </summary>
    public IssueKind? ConflictingTriageKind =>
        _labelledKind is not null && TriageVerdict is { } verdict && verdict.Kind != _labelledKind
            ? verdict.Kind
            : null;

    public MonitoredRepositoryId MonitoredRepositoryId { get; private set; }

    public int IssueNumber { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public IssueAuthor Author { get; private set; } = null!;

    public ProviderUrl Url { get; private set; } = null!;

    public IReadOnlyList<string> Labels { get; private set; } = [];

    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>
    /// When the issue entered its current state: the transition that created this state instance, or
    /// <see cref="DetectedAt"/> for the detected state. Never carried over from the state being left.
    /// </summary>
    public DateTimeOffset EnteredStateAt { get; private set; }

    public IReadOnlyList<int> BlockedBy { get; private set; } = [];

    /// <summary>
    /// The issue's append-only clarification log (ADR 0096): every round a run asked a person, oldest first. Carried
    /// through every transition with the shared properties, so an answer lasts as long as the issue does.
    /// </summary>
    public IReadOnlyList<Clarification> Clarifications { get; private set; } = [];

    /// <summary>
    /// True once a person's review feedback has driven a revision (issue #917): Foundry then never merges the pull
    /// request on its own, and the issue waits in review for the person. Set only by a review-feedback revision and
    /// carried through every transition with the shared properties, so no detour (a failed run, a question, a retry)
    /// drops it.
    /// </summary>
    public bool HasPersonFeedback { get; private set; }

    /// <summary>
    /// How many times the issue's Implement step has run against published work, starting at 1. A revision trigger
    /// (review findings, a person's feedback, failed checks, a rebase conflict) starts a new round; a retry, a resume
    /// after answers and a continuation stay in the same round. Carried through every transition with the shared
    /// properties.
    /// </summary>
    public int Round { get; private set; } = 1;

    /// <summary>
    /// The URL of the pull request Foundry marked ready for review, or <see langword="null"/> when it marked none.
    /// Carried through every transition with the shared properties; only <see cref="IsPullRequestMarkedReady"/> tells
    /// whether it is the issue's current pull request.
    /// </summary>
    public string? MarkedReadyPullRequestUrl { get; private set; }

    /// <summary>
    /// True when Foundry marked the issue's current pull request ready for review. Keyed to the URL, so a new pull
    /// request (a fresh run after a retry) is unmarked without any reset, and a person's later re-draft stands.
    /// </summary>
    public bool IsPullRequestMarkedReady =>
        LinkedPullRequestUrl is { } linked && linked == MarkedReadyPullRequestUrl;

    /// <summary>
    /// The most review-driven revisions (<see cref="RevisionTrigger.ReviewFindings"/>) a round of review may start
    /// before a changes-requested verdict goes to a person instead (ADR 0113). Bounds a reviewer/implementer loop.
    /// </summary>
    public const int ReviewMaxFixRuns = 2;

    /// <summary>
    /// How many review-driven revisions the current round has started. Carried through every transition with the
    /// shared properties, so no detour (a failed run, a question, a rebase) resets it; only a person-triggered round
    /// (a person's review feedback, or a push while in review) starts it again at zero.
    /// </summary>
    public int ReviewFixAttempts { get; private set; }

    /// <summary>
    /// How many checks fix runs (<see cref="RevisionTrigger.FailedChecks"/>) the current pushed code has spent, capped
    /// by <see cref="ChecksPendingIssue.ChecksMaxFixPushes"/>. Carried through every transition with the shared
    /// properties, so no detour drops it; only new code (a fresh run, a person's push or feedback, a findings fix)
    /// starts it again at zero.
    /// </summary>
    public int ChecksFixAttempts { get; private set; }

    /// <summary>
    /// The issue's most recent review verdict (ADR 0113), or <see langword="null"/> when no review completed. Carried
    /// through every transition with the shared properties, so a later review of an unchanged change can carry it.
    /// </summary>
    public LatestReview? LatestReview { get; private set; }

    /// <summary>The round still waiting for input, or <see langword="null"/> when every round is answered.</summary>
    public Clarification? OpenClarification =>
        Clarifications.Count > 0 && Clarifications[^1].IsOpen ? Clarifications[^1] : null;

    /// <summary>
    /// The URL of the pull request this issue carries, or <c>null</c> when the current state has none.
    /// Every state that stores a PR URL overrides this beside its stored field, normalizing an empty
    /// stored value (e.g. a continuable failure or completion without a PR) to <c>null</c>.
    /// </summary>
    public virtual string? LinkedPullRequestUrl => null;

    /// <summary>The part of the lifecycle that currently owns the issue.</summary>
    public abstract IssueStage Stage { get; }

    /// <summary>True when the issue progresses only after a person acts.</summary>
    public abstract bool NeedsAction { get; }

    /// <summary>
    /// True when the issue is waiting for a worker run to be dispatched: queued, or detected or blocked before it can
    /// be queued.
    /// </summary>
    public abstract bool AwaitsDispatch { get; }

    /// <summary>
    /// True when the issue waits for its open blockers to close before it can move on: blocked, or blocked before
    /// triage.
    /// </summary>
    public abstract bool WaitsOnBlockers { get; }

    /// <summary>The step Foundry's own code is running for the issue, or <see langword="null"/> when none is.</summary>
    public abstract IssueCodeStep? CodeStep { get; }

    /// <summary>
    /// Why the issue entered this state, as a wire token: a failure's category, a revision's or publish's trigger, a
    /// pending or diverged state's reason, or the checks, merge or review outcome that led here. <see langword="null"/>
    /// for plain progress. Recorded on the issue's step transition log.
    /// </summary>
    public abstract string? TransitionCause { get; }

    /// <summary>
    /// The issue's current step on its lifecycle path. A working issue whose run publishes sits on
    /// <see cref="IssueLifecycleStep.Publish"/> instead, read from the run by <see cref="IssueLifecyclePath"/>.
    /// </summary>
    public abstract IssueLifecycleStep LifecycleStep { get; }

    /// <summary>
    /// The kind of worker run the issue will run when it is next dispatched, or <see langword="null"/> when the state
    /// has no run ahead (in-flight states also return <see langword="null"/> for now).
    /// </summary>
    public abstract RunKind? RunKind { get; }

    private protected static string? NullIfEmpty(string value) =>
        string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// Returns true for states that can be safely hard-deleted when a provider untrack event is received.
    /// Active states (in_progress, revision_in_progress, triage_in_progress, review) return false — a live worker is running or
    /// the issue is under active review. The terminal state completed returns false — completion wins over
    /// provider closure. publish_failed and publish_blocked return false — their run still holds unpublished work. All other states
    /// (including unchanged) return true and are hard-deleted on untrack.
    /// QueuedIssue covers the queued variants (queued, triage_queued, revision_queued, continuation_queued) — any future
    /// queued tier added under QueuedIssue is included by default, which is the correct safe default. review_queued is the
    /// exception: its pull request is open, so like review its provider closure completes it rather than deleting it.
    /// </summary>
    public bool IsRestingState() =>
        this is DetectedIssue
            or QueuedIssue and not ReviewQueuedIssue
            or BlockedIssue
            or TriageBlockedIssue
            or FailedIssue
            or TriageFailedIssue
            or ContinuableFailedIssue
            or RevisionFailedIssue
            or BranchDivergedIssue
            or UnchangedIssue;

    internal void SetBlockedBy(IReadOnlyList<int> blockers)
    {
        BlockedBy = blockers.Count <= TriageVerdict.Blocked.MaxBlockers
            ? blockers
            : blockers.Take(TriageVerdict.Blocked.MaxBlockers).ToList();
    }

    public void UpdateDetails(string title, IReadOnlyList<string> labels, KindClassification kindClassification)
    {
        Title = title;
        Labels = labels;
        _labelledKind = LabelledKindOf(kindClassification);
    }

    protected void SetSharedProperties(
        MonitoredRepositoryId monitoredRepositoryId,
        int issueNumber,
        string title,
        IssueAuthor author,
        ProviderUrl url,
        IReadOnlyList<string> labels,
        DateTimeOffset detectedAt,
        KindClassification kindClassification)
    {
        MonitoredRepositoryId = monitoredRepositoryId;
        IssueNumber = issueNumber;
        Title = title;
        Author = author;
        Url = url;
        Labels = labels;
        DetectedAt = detectedAt;
        _labelledKind = LabelledKindOf(kindClassification);
    }

    /// <summary>Carries every shared property, the clarification log included, from the state being left.</summary>
    private protected void CopySharedPropertiesFrom(Issue source)
    {
        SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt,
            KindClassification.Unclassified);
        _labelledKind = source._labelledKind;
        TriageVerdict = source.TriageVerdict;
        Clarifications = source.Clarifications;
        HasPersonFeedback = source.HasPersonFeedback;
        Round = source.Round;
        MarkedReadyPullRequestUrl = source.MarkedReadyPullRequestUrl;
        ReviewFixAttempts = source.ReviewFixAttempts;
        ChecksFixAttempts = source.ChecksFixAttempts;
        LatestReview = source.LatestReview;
    }

    /// <summary>
    /// Records that Foundry marked <paramref name="pullRequestUrl"/> ready for review. Only the issue's current pull
    /// request is recorded, so a late event for an earlier pull request cannot unmark the current one. Returns
    /// <see langword="false"/> when nothing changed: the issue holds no such pull request, or it is already recorded.
    /// </summary>
    public bool RecordPullRequestMarkedReady(string pullRequestUrl)
    {
        if (LinkedPullRequestUrl is not { } linked || linked != pullRequestUrl || IsPullRequestMarkedReady)
        {
            return false;
        }

        MarkedReadyPullRequestUrl = linked;
        return true;
    }

    /// <summary>A revision trigger sends the issue back to implement: a new round starts.</summary>
    private protected void StartRound()
    {
        Round++;
    }

    /// <summary>A checks fix run is queued: it spends one of the pushed code's checks fix runs.</summary>
    private protected void RecordChecksFix()
    {
        ChecksFixAttempts++;
    }

    /// <summary>New code reached the branch: the checks get their full fix budget again.</summary>
    private protected void ResetChecksFixAttempts()
    {
        ChecksFixAttempts = 0;
    }

    /// <summary>
    /// No new code follows: a red verdict goes to a person instead of spending a fix run on code nobody changed
    /// (ADR 0108).
    /// </summary>
    private protected void ExhaustChecksFixAttempts()
    {
        ChecksFixAttempts = ChecksPendingIssue.ChecksMaxFixPushes;
    }

    /// <summary>Records the verdict a review run completed with; the next state carries it.</summary>
    private protected void RecordLatestReview(LatestReview latestReview)
    {
        ArgumentNullException.ThrowIfNull(latestReview);
        LatestReview = latestReview;
    }

    /// <summary>The reviewer's findings drive this revision: it spends one of the round's review fix runs.</summary>
    private protected void RecordReviewFix()
    {
        ReviewFixAttempts++;
    }

    /// <summary>A person started a new round of review: the reviewer gets its full fix budget again.</summary>
    private protected void ResetReviewFixAttempts()
    {
        ReviewFixAttempts = 0;
    }

    /// <summary>
    /// A person's review feedback drives this revision: Foundry no longer merges on its own, and the person's round
    /// gives the reviewer its full fix budget again.
    /// </summary>
    private protected void RecordPersonFeedback()
    {
        HasPersonFeedback = true;
        ResetReviewFixAttempts();
    }

    // Only a label decides the label half: a classification derived from labels is Labelled or Unclassified.
    private static IssueKind? LabelledKindOf(KindClassification kindClassification) =>
        kindClassification is KindClassification.Labelled labelled ? labelled.Kind : null;

    /// <summary>Records the latest triage verdict; its kind decides the issue's kind only while no label does.</summary>
    private protected void RecordTriageVerdict(TriageVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        TriageVerdict = verdict;
    }

    /// <summary>
    /// Leaves triage through the dependency check (ADR 0097): queued when the issue has no open blockers, blocked
    /// otherwise, so an issue with open blockers is never queued. A ready <paramref name="verdict"/> is recorded on the
    /// next state; skipping triage records none.
    /// </summary>
    private protected Issue ReleaseThroughDependencyCheck(TriageVerdict.Ready? verdict, DateTimeOffset transitionedAt)
    {
        if (BlockedBy.Count == 0)
        {
            FreshQueuedIssue queued = FreshQueuedIssue.FromTriage(this, verdict, transitionedAt);
            AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
            return queued;
        }

        BlockedIssue blocked = BlockedIssue.FromTriage(this, verdict, transitionedAt);
        AddDomainEvent(new Events.IssueBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }

    private protected void AskClarification(IReadOnlyList<InputQuestion> questions, DateTimeOffset askedAt)
    {
        // A round left open when its issue moved on without input (closed, or its pull request closed) stays open in
        // the log; the new round is appended after it.
        Clarifications = [.. Clarifications, Clarification.Ask(questions, askedAt)];
    }

    private protected void AnswerClarification(ClarificationInput input, DateTimeOffset answeredAt)
    {
        Clarification open = OpenClarification
            ?? throw new InvalidOperationException("No clarification round is open.");

        Clarifications = [.. Clarifications.Take(Clarifications.Count - 1), open.Answer(input, answeredAt)];
    }
}
