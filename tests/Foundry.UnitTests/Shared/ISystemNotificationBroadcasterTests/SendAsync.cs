using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.ISystemNotificationBroadcasterTests;

public sealed class SendAsync
{
    [Fact]
    public async Task WhenCalled_InvokesImplementation()
    {
        // Arrange
        SystemNotification notification = new("auth", true, "Claude authentication is invalid.");
        StubBroadcaster sut = new();

        // Act
        await sut.SendAsync(notification, CancellationToken.None);

        // Assert
        sut.SentNotification.ShouldBe(notification);
    }

    private sealed class StubBroadcaster : ISystemNotificationBroadcaster
    {
        public SystemNotification? SentNotification { get; private set; }

        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
        {
            SentNotification = notification;
            return Task.CompletedTask;
        }
    }
}
