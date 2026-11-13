using Foundry.Shared;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>Records each system notification sent.</summary>
internal sealed class CapturingSystemNotificationBroadcaster : ISystemNotificationBroadcaster
{
    private readonly List<SystemNotification> _notifications = [];

    public IReadOnlyList<SystemNotification> SentNotifications => _notifications;

    public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
    {
        _notifications.Add(notification);
        return Task.CompletedTask;
    }
}
