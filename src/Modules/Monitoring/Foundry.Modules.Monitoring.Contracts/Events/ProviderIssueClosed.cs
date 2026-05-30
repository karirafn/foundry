using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

public sealed record ProviderIssueClosed(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber) : IIntegrationEvent;
