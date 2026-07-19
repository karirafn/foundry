using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.DockerAvailability;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.DockerAvailability.DockerAvailabilityChangedBroadcastHandlerTests;

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
    public async Task WhenDockerBecamesAvailable_SetsStateIsAvailableTrue()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        DockerAvailabilityState state = new();
        DockerAvailabilityChangedBroadcastHandler sut = new(state, broadcaster);
        DockerAvailabilityChanged @event = new(IsAvailable: true);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        ((IDockerAvailabilityState)state).IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenDockerBecamesUnavailable_SetsStateIsAvailableFalse()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        DockerAvailabilityState state = new();
        DockerAvailabilityChangedBroadcastHandler sut = new(state, broadcaster);
        DockerAvailabilityChanged @event = new(IsAvailable: false);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        ((IDockerAvailabilityState)state).IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenDockerBecamesAvailable_SendsDockerCategoryNotificationWithIsActiveFalse()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        DockerAvailabilityState state = new();
        DockerAvailabilityChangedBroadcastHandler sut = new(state, broadcaster);
        DockerAvailabilityChanged @event = new(IsAvailable: true);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.ShouldSatisfyAllConditions(
            () => notification.Category.ShouldBe("docker"),
            () => notification.IsActive.ShouldBeFalse(),
            () => notification.Message.ShouldBe(string.Empty));
    }

    [Fact]
    public async Task WhenDockerBecamesUnavailable_SendsDockerCategoryNotificationWithIsActiveTrue()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        DockerAvailabilityState state = new();
        DockerAvailabilityChangedBroadcastHandler sut = new(state, broadcaster);
        DockerAvailabilityChanged @event = new(IsAvailable: false);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.ShouldSatisfyAllConditions(
            () => notification.Category.ShouldBe("docker"),
            () => notification.IsActive.ShouldBeTrue(),
            () => notification.Message.ShouldBe(string.Empty));
    }

    [Fact]
    public async Task WhenHandled_ForwardsCancellationToken()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        DockerAvailabilityState state = new();
        DockerAvailabilityChangedBroadcastHandler sut = new(state, broadcaster);
        DockerAvailabilityChanged @event = new(IsAvailable: true);
        using CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;

        // Act
        await sut.HandleAsync(@event, token);

        // Assert
        broadcaster.LastToken.ShouldBe(token);
    }
}
