using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Issues.Domain;

public abstract class Issue : AggregateRoot<IssueId>, IStateMachine<Issue>
{
    // Private parameterless constructor for EF Core materialization.
    private protected Issue() : base(IssueId.New())
    {
    }

    protected Issue(IssueId id) : base(id)
    {
    }

    public MonitoredRepositoryId MonitoredRepositoryId { get; private set; }

    public int IssueNumber { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public IssueAuthor Author { get; private set; } = null!;

    public ProviderUrl Url { get; private set; } = null!;

    public IReadOnlyList<string> Labels { get; private set; } = [];

    public DateTimeOffset DetectedAt { get; private set; }

    public IReadOnlyList<int> BlockedBy { get; private set; } = [];

    internal void SetBlockedBy(IReadOnlyList<int> blockers)
    {
        BlockedBy = blockers;
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
