using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.ScheduledImageBuildFailedBroadcastHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenAScheduledRebuildFailed_SendsAnInactiveImageBuildNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        ScheduledImageBuildFailedBroadcastHandler sut = new(broadcaster);

        // Act
        await sut.HandleAsync(new ScheduledImageBuildFailed("apt failed", null, 1), CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.ShouldSatisfyAllConditions(
            () => notification.Category.ShouldBe("image-build"),
            () => notification.IsActive.ShouldBeFalse());
    }
}
