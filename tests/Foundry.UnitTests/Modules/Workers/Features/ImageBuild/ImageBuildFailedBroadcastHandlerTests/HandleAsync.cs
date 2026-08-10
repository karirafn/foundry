using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.ImageBuildFailedBroadcastHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenImageBuildFailed_SendsImageBuildCategoryNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        ImageBuildFailedBroadcastHandler sut = new(broadcaster);
        ImageBuildFailed @event = new(ErrorTail: "some error", NextRetryAt: DateTimeOffset.UtcNow.AddMinutes(5), Attempt: 1);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.Category.ShouldBe("image-build");
    }

    [Fact]
    public async Task WhenImageBuildFailed_SendsIsActiveTrueNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        ImageBuildFailedBroadcastHandler sut = new(broadcaster);
        ImageBuildFailed @event = new(ErrorTail: "some error", NextRetryAt: DateTimeOffset.UtcNow.AddMinutes(5), Attempt: 1);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenImageBuildFailed_SendsEmptyMessageNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        ImageBuildFailedBroadcastHandler sut = new(broadcaster);
        ImageBuildFailed @event = new(ErrorTail: "some error", NextRetryAt: DateTimeOffset.UtcNow.AddMinutes(5), Attempt: 1);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.Message.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task WhenImageBuildFailed_ForwardsCancellationToken()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        ImageBuildFailedBroadcastHandler sut = new(broadcaster);
        ImageBuildFailed @event = new(ErrorTail: null, NextRetryAt: null, Attempt: 0);
        using CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;

        // Act
        await sut.HandleAsync(@event, token);

        // Assert
        broadcaster.LastToken.ShouldBe(token);
    }
}
