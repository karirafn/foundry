using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Events;

public sealed record ProviderIssueUntracked(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber) : IIntegrationEvent;
