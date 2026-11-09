using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
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

    // The persisted shape of KindClassification: the labelled kind, or null when Unclassified.
    private IssueKind? _labelledKind;

    public KindClassification KindClassification
    {
        get => _labelledKind is null
            ? KindClassification.Unclassified
            : new KindClassification.Labelled(_labelledKind);
        private set => _labelledKind = value is KindClassification.Labelled labelled
            ? labelled.Kind
            : null;
    }

    public MonitoredRepositoryId MonitoredRepositoryId { get; private set; }

    public int IssueNumber { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public IssueAuthor Author { get; private set; } = null!;

    public ProviderUrl Url { get; private set; } = null!;

    public IReadOnlyList<string> Labels { get; private set; } = [];

    public DateTimeOffset DetectedAt { get; private set; }

    public IReadOnlyList<int> BlockedBy { get; private set; } = [];

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
    /// Active states (in_progress, revision_in_progress, review) return false — a live worker is running or
    /// the issue is under active review. The terminal state completed returns false — completion wins over
    /// provider closure. All other states (including unchanged) return true and are hard-deleted on untrack.
    /// QueuedIssue covers all queued variants (queued, revision_queued, continuation_queued) — any future
    /// queued tier added under QueuedIssue is included by default, which is the correct safe default.
    /// </summary>
    public bool IsRestingState() =>
        this is DetectedIssue
            or QueuedIssue
            or BlockedIssue
            or FailedIssue
            or ContinuableFailedIssue
            or RevisionFailedIssue
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
        KindClassification = kindClassification;
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
        KindClassification = kindClassification;
    }
}
