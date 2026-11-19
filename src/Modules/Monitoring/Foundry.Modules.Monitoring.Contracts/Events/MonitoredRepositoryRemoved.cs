using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// A monitored repository was removed from Foundry (ADR 0117): Issues deletes its non-completed issues and Workers
/// discards its unpublished runs. Completed issues and every worker run stay attached to the removed repository.
/// </summary>
public sealed record MonitoredRepositoryRemoved(MonitoredRepositoryId RepositoryId, DateTimeOffset RemovedAt) : IIntegrationEvent;
