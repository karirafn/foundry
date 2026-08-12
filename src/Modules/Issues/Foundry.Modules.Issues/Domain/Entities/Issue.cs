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

    public IssueKind IssueKind { get; protected set; } = IssueKind.Feature;

    public MonitoredRepositoryId MonitoredRepositoryId { get; private set; }

    public int IssueNumber { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public IssueAuthor Author { get; private set; } = null!;

    public ProviderUrl Url { get; private set; } = null!;

    public IReadOnlyList<string> Labels { get; private set; } = [];

    public DateTimeOffset DetectedAt { get; private set; }

    public IReadOnlyList<int> BlockedBy { get; private set; } = [];

    // GitHub limits dependencies to 50 per direction; cap silently to match.
    private const int MaxBlockers = 50;

    /// <summary>
    /// Returns true for states that can be safely hard-deleted when a provider untrack event is received.
    /// Active states (in_progress, revision_in_progress, review) return false — a live worker is running or
    /// the issue is under active review. The terminal state completed returns false — completion wins over
    /// provider closure. All other states (including unchanged) return true and are hard-deleted on untrack.
    /// </summary>
    public bool IsRestingState() =>
        this is DetectedIssue
            or QueuedIssue
            or BlockedIssue
            or FailedIssue
            or ContinuableFailedIssue
            or RevisionFailedIssue
            or RevisionQueuedIssue
            or ContinuationQueuedIssue
            or UnchangedIssue;

    internal void SetBlockedBy(IReadOnlyList<int> blockers)
    {
        BlockedBy = blockers.Count <= MaxBlockers
            ? blockers
            : blockers.Take(MaxBlockers).ToList();
    }

    public void UpdateDetails(string title, string body, IReadOnlyList<string> labels)
    {
        Title = title;
        Body = body;
        Labels = labels;
    }

    protected void SetSharedProperties(
        MonitoredRepositoryId monitoredRepositoryId,
        int issueNumber,
        string title,
        string body,
        IssueAuthor author,
        ProviderUrl url,
        IReadOnlyList<string> labels,
        DateTimeOffset detectedAt)
    {
        MonitoredRepositoryId = monitoredRepositoryId;
        IssueNumber = issueNumber;
        Title = title;
        Body = body;
        Author = author;
        Url = url;
        Labels = labels;
        DetectedAt = detectedAt;
    }
}
