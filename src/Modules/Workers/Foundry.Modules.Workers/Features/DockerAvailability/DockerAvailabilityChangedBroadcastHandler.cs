using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.DockerAvailability;

internal sealed class DockerAvailabilityChangedBroadcastHandler(
    DockerAvailabilityState state,
    ISystemNotificationBroadcaster broadcaster) : IIntegrationEventHandler<DockerAvailabilityChanged>
{
    private const string DockerCategory = "docker";

    public Task HandleAsync(DockerAvailabilityChanged @event, CancellationToken cancellationToken)
    {
        state.Set(@event.IsAvailable);

        return broadcaster.SendAsync(
            new SystemNotification(DockerCategory, IsActive: !@event.IsAvailable, Message: string.Empty),
            cancellationToken);
    }
}
