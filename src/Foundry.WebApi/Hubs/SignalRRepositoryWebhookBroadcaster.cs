using Foundry.Modules.Monitoring.Contracts;

using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

internal sealed class SignalRRepositoryWebhookBroadcaster(IHubContext<SystemNotificationHub> hubContext)
    : IRepositoryWebhookBroadcaster
{
    private const string RepositoryWebhookChangedMethod = "RepositoryWebhookChanged";
    private const string RepositoryWebhooksChangedMethod = "RepositoryWebhooksChanged";

    public Task RepositoryWebhookChangedAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken)
        => hubContext.Clients.All.SendAsync(
            RepositoryWebhookChangedMethod,
            new RepositoryWebhookChangedMessage(repositoryId.Value),
            cancellationToken);

    public Task RepositoryWebhooksChangedAsync(CancellationToken cancellationToken)
        => hubContext.Clients.All.SendAsync(RepositoryWebhooksChangedMethod, cancellationToken);

    private sealed record RepositoryWebhookChangedMessage(Guid RepositoryId);
}
