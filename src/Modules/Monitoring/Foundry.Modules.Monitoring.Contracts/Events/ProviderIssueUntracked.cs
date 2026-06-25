using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

public sealed record ProviderIssueUntracked(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber) : IIntegrationEvent;
