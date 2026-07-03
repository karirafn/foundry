using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.DispatchPausedForAuthInvalidBroadcastHandlerTests;

public sealed class HandleAsync
{
    private sealed class CapturingSystemNotificationBroadcaster : ISystemNotificationBroadcaster
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

    [Fact]
    public async Task WhenHandled_CallsSendAsyncExactlyOnce()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        DispatchPausedForAuthInvalidBroadcastHandler sut = new(broadcaster);
        DispatchPausedForAuthInvalid @event = new();

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        broadcaster.SentNotifications.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenHandled_SendsDispatchCategoryNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        DispatchPausedForAuthInvalidBroadcastHandler sut = new(broadcaster);
        DispatchPausedForAuthInvalid @event = new();

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.Category.ShouldBe("dispatch");
    }

    [Fact]
    public async Task WhenHandled_SendsIsActiveTrueNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        DispatchPausedForAuthInvalidBroadcastHandler sut = new(broadcaster);
        DispatchPausedForAuthInvalid @event = new();

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenHandled_SendsEmptyMessageNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        DispatchPausedForAuthInvalidBroadcastHandler sut = new(broadcaster);
        DispatchPausedForAuthInvalid @event = new();

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.Message.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task WhenHandled_ForwardsCancellationToken()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        DispatchPausedForAuthInvalidBroadcastHandler sut = new(broadcaster);
        DispatchPausedForAuthInvalid @event = new();
        using CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;

        // Act
        await sut.HandleAsync(@event, token);

        // Assert
        broadcaster.LastToken.ShouldBe(token);
    }
}
