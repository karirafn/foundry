using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Events;

public sealed record CircularDependencyDetected(
    MonitoredRepositoryId MonitoredRepositoryId,
    IReadOnlyList<int> CyclePath) : IDomainEvent;
