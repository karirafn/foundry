using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Events;

/// <summary>A monitored repository was removed from Foundry (ADR 0117).</summary>
internal sealed record MonitoredRepositoryRemovedDomainEvent(MonitoredRepositoryId RepositoryId, DateTimeOffset RemovedAt)
    : IDomainEvent;
