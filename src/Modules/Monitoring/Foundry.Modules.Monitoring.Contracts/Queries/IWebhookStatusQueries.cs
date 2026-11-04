namespace Foundry.Modules.Monitoring.Contracts.Queries;

/// <summary>Reads a repository's effective Webhook Status (ADR 0085) for other modules.</summary>
public interface IWebhookStatusQueries
{
    /// <summary>
    /// Whether the repository's effective Webhook Status is <c>active</c>: webhook ingress is configured
    /// and a verified delivery has arrived with no missed change found since. <see langword="false"/> for
    /// an unknown repository.
    /// </summary>
    Task<bool> IsWebhookActiveAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken);
}
