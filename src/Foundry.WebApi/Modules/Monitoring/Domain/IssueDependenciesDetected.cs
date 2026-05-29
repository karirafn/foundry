using Foundry.Shared;

namespace Foundry.WebApi.Modules.Monitoring.Domain;

public sealed record IssueDependenciesDetected(
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    IReadOnlyList<int> BlockedByIssueNumbers) : IDomainEvent;
