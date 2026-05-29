using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.WebApi.Modules.Issues.Domain;

public sealed record CircularDependencyDetected(
    MonitoredRepositoryId MonitoredRepositoryId,
    IReadOnlyList<int> CyclePath) : IDomainEvent;
