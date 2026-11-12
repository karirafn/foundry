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

    protected Issue(IssueId id) : base(id)
    {
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

    public IReadOnlyList<int> BlockedBy { get; private set; } = [];

    /// <summary>
    /// The issue's append-only clarification log (ADR 0096): every round a run asked a person, oldest first. Carried
    /// through every transition with the shared properties, so an answer lasts as long as the issue does.
    /// </summary>
    public IReadOnlyList<Clarification> Clarifications { get; private set; } = [];

    /// <summary>The round still waiting for input, or <see langword="null"/> when every round is answered.</summary>
    public Clarification? OpenClarification =>
        Clarifications.Count > 0 && Clarifications[^1].IsOpen ? Clarifications[^1] : null;

    /// <summary>
    /// The URL of the pull request this issue carries, or <c>null</c> when the current state has none.
    /// Every state that stores a PR URL overrides this beside its stored field, normalizing an empty
    /// stored value (e.g. a continuable failure or completion without a PR) to <c>null</c>.
    /// </summary>
    public virtual string? LinkedPullRequestUrl => null;

    private protected static string? NullIfEmpty(string value) =>
        string.IsNullOrEmpty(value) ? null : value;

    // GitHub limits dependencies to 50 per direction; cap silently to match.
    private const int MaxBlockers = 50;

    /// <summary>
    /// Returns true for states that can be safely hard-deleted when a provider untrack event is received.
    /// Active states (in_progress, revision_in_progress, triage_in_progress, review) return false — a live worker is running or
    /// the issue is under active review. The terminal state completed returns false — completion wins over
    /// provider closure. publish_failed and publish_blocked return false — their run still holds unpublished work. All other states
    /// (including unchanged) return true and are hard-deleted on untrack.
    /// QueuedIssue covers all queued variants (queued, triage_queued, revision_queued, continuation_queued) — any future
    /// queued tier added under QueuedIssue is included by default, which is the correct safe default.
    /// </summary>
    public bool IsRestingState() =>
        this is DetectedIssue
            or QueuedIssue
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
        BlockedBy = blockers.Count <= MaxBlockers
            ? blockers
            : blockers.Take(MaxBlockers).ToList();
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
    private protected Issue ReleaseThroughDependencyCheck(TriageVerdict.Ready? verdict)
    {
        if (BlockedBy.Count == 0)
        {
            FreshQueuedIssue queued = FreshQueuedIssue.FromTriage(this, verdict);
            AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
            return queued;
        }

        BlockedIssue blocked = BlockedIssue.FromTriage(this, verdict);
        AddDomainEvent(new Events.IssueBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }

    private protected void AskClarification(IReadOnlyList<InputQuestion> questions, DateTimeOffset askedAt)
    {
        // A round left open when its issue moved on without input (closed, or its pull request closed) stays open in
        // the log; the new round is appended after it.
        Clarifications = [.. Clarifications, Clarification.Ask(questions, askedAt)];
    }

    private protected void AnswerClarification(string input, DateTimeOffset answeredAt)
    {
        Clarification open = OpenClarification
            ?? throw new InvalidOperationException("No clarification round is open.");

        Clarifications = [.. Clarifications.Take(Clarifications.Count - 1), open.Answer(input, answeredAt)];
    }
}
