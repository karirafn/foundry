using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Events;

public sealed record IssueDependenciesDetected(
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    IReadOnlyList<int> BlockedByIssueNumbers) : IIntegrationEvent;
