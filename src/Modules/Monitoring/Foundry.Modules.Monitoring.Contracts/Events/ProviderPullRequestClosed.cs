using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Events;

public sealed record ProviderPullRequestClosed(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber) : IIntegrationEvent;
