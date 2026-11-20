namespace Foundry.Modules.Monitoring.Contracts.Queries;

public interface IRepositoryRemovalQueries
{
    /// <summary>
    /// Whether the repository has been removed (ADR 0117). Reads the row whatever its removal state, so a claim that
    /// reads it inside its write transaction is serialized against a removal of the same repository. An unknown id is
    /// not removed.
    /// </summary>
    Task<bool> IsRemovedAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken);
}
