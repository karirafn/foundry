namespace Foundry.Shared;

public interface ISystemNotificationBroadcaster
{
    Task SendAsync(SystemNotification notification, CancellationToken cancellationToken);
}
