using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

public sealed record PullRequestChangesRequested(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    IReadOnlyList<ReviewComment> Comments,
    int OmittedCommentCount = 0,
    DateTimeOffset? NewestCommentAt = null) : IIntegrationEvent;
