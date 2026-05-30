using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

public sealed record ProviderPullRequestClosed(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber) : IIntegrationEvent;
