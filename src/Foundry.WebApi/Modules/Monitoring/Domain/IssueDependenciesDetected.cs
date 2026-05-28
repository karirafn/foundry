using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Monitoring.Domain;

public sealed record IssueDependenciesDetected(
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    IReadOnlyList<int> BlockedByIssueNumbers) : IDomainEvent;
