using Foundry.Modules.Credentials.Features.Broadcasts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Broadcasts.WorkerCreditsExhaustedBroadcastHandlerTests;

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
    public async Task WhenHandled_SendsCreditsCategoryNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        WorkerCreditsExhaustedBroadcastHandler sut = new(broadcaster);
        WorkerCreditsExhausted @event = new(WorkerRunId.New(), Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.Category.ShouldBe("credits");
    }

    [Fact]
    public async Task WhenHandled_SendsIsActiveTrueNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        WorkerCreditsExhaustedBroadcastHandler sut = new(broadcaster);
        WorkerCreditsExhausted @event = new(WorkerRunId.New(), Guid.NewGuid());

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
        WorkerCreditsExhaustedBroadcastHandler sut = new(broadcaster);
        WorkerCreditsExhausted @event = new(WorkerRunId.New(), Guid.NewGuid());

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
        WorkerCreditsExhaustedBroadcastHandler sut = new(broadcaster);
        WorkerCreditsExhausted @event = new(WorkerRunId.New(), Guid.NewGuid());
        using CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;

        // Act
        await sut.HandleAsync(@event, token);

        // Assert
        broadcaster.LastToken.ShouldBe(token);
    }
}
