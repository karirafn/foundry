using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// The provider refused the account token (HTTP 403, or 404 where it hides a repository the token cannot see) when a
/// run's Prepare fetch, push or pull-request call used it. Monitoring resets the repository's <c>Granted</c> write probe
/// so dispatch stops and the next poll re-probes (ADR 0054). The repository id is a <see cref="Guid"/> because
/// Workers.Contracts does not reference Monitoring.Contracts.
/// </summary>
public sealed record WorkerRunAccessRefused(WorkerRunId WorkerRunId, Guid MonitoredRepositoryId) : IIntegrationEvent;
