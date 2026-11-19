using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Settings.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Webhooks.StatusBroadcast;

/// <summary>
/// Tells the dashboard that every repository's webhook status may have changed after the Webhook Public
/// Base URL changes. Setting it the first time moves every repository off the derived <c>not-configured</c>
/// status even when its registration fails and its stored status stays unchanged, so no per-repository
/// broadcast would reach those rows.
/// </summary>
[IntegrationEventHandlerIdentity("Monitoring.WebhookPublicBaseUrlChangedBroadcast")]
internal sealed class WebhookPublicBaseUrlChangedBroadcastHandler(IRepositoryWebhookBroadcaster broadcaster)
    : IIntegrationEventHandler<WebhookPublicBaseUrlChanged>
{
    public Task HandleAsync(WebhookPublicBaseUrlChanged @event, CancellationToken cancellationToken)
        => broadcaster.RepositoryWebhooksChangedAsync(cancellationToken);
}
