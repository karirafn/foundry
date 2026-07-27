using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Events;

public sealed record ProviderIssueClosed(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber) : IIntegrationEvent;
