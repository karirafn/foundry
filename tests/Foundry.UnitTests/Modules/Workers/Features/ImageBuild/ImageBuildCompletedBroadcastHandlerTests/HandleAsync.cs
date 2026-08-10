using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.ImageBuildCompletedBroadcastHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenImageBuildCompleted_SendsImageBuildCategoryNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        ImageBuildCompletedBroadcastHandler sut = new(broadcaster);
        ImageBuildCompleted @event = new();

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.Category.ShouldBe("image-build");
    }

    [Fact]
    public async Task WhenImageBuildCompleted_SendsIsActiveFalseNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        ImageBuildCompletedBroadcastHandler sut = new(broadcaster);
        ImageBuildCompleted @event = new();

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenImageBuildCompleted_SendsEmptyMessageNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        ImageBuildCompletedBroadcastHandler sut = new(broadcaster);
        ImageBuildCompleted @event = new();

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.Message.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task WhenImageBuildCompleted_ForwardsCancellationToken()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        ImageBuildCompletedBroadcastHandler sut = new(broadcaster);
        ImageBuildCompleted @event = new();
        using CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;

        // Act
        await sut.HandleAsync(@event, token);

        // Assert
        broadcaster.LastToken.ShouldBe(token);
    }
}
