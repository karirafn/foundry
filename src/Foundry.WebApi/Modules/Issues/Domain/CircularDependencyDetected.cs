using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Issues.Domain;

public sealed record CircularDependencyDetected(
    MonitoredRepositoryId MonitoredRepositoryId,
    IReadOnlyList<int> CyclePath) : IDomainEvent;
