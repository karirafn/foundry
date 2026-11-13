using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// A review issue's pull request no longer has the head commit its checks verdict was read against (or none was
/// stored), so the verdict no longer describes the pull request.
/// </summary>
public sealed record ProviderPullRequestHeadMoved(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber) : IIntegrationEvent;
