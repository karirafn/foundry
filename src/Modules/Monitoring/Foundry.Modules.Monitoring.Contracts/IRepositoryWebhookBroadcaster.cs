namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Tells connected dashboards that a repository's webhook status changed. Carries no status: a client
/// re-reads the committed state, so broadcasts arriving out of order never leave a stale value behind.
/// </summary>
public interface IRepositoryWebhookBroadcaster
{
    Task RepositoryWebhookChangedAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken);

    /// <summary>Every repository's displayed webhook status may have changed.</summary>
    Task RepositoryWebhooksChangedAsync(CancellationToken cancellationToken);
}
