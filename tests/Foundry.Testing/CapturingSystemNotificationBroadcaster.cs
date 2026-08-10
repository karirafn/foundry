using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// A test double for <see cref="ISystemNotificationBroadcaster"/> that captures all sent notifications
/// and the last cancellation token for assertion in tests.
/// </summary>
public sealed class CapturingSystemNotificationBroadcaster : ISystemNotificationBroadcaster
{
    private readonly List<SystemNotification> _notifications = [];
    private CancellationToken _lastToken;

    public IReadOnlyList<SystemNotification> SentNotifications => _notifications;
    public CancellationToken LastToken => _lastToken;

    public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
    {
        _notifications.Add(notification);
        _lastToken = cancellationToken;
        return Task.CompletedTask;
    }
}
