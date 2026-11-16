using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageToolchainRecorderTests;

public sealed class Notification
{
    private const string ImageId = "sha256:aaaa";

    [Fact]
    public async Task WhenAListIsStored_SendsTheImageBuildNotification()
    {
        // Arrange
        await using RecorderFixture fixture = new(ImageId);
        fixture.Probe.Returning(RecorderFixture.DockerOnly());

        // Act
        bool recorded = await fixture.Recorder.TryRecordAsync(ImageId, TestContext.Current.CancellationToken);

        // Assert
        recorded.ShouldBeTrue();
        SystemNotification notification = fixture.Broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.Category.ShouldBe(WorkerImageRebuildService.ImageBuildCategory);
    }

    [Fact]
    public async Task WhenTheImageAlreadyHasAList_SendsNothing()
    {
        // Arrange
        await using RecorderFixture fixture = new(ImageId);
        await fixture.StoreAsync(ImageId, RecorderFixture.DockerOnly());

        // Act
        await fixture.Recorder.TryRecordAsync(ImageId, TestContext.Current.CancellationToken);

        // Assert
        fixture.Broadcaster.SentNotifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheProbeFails_SendsNothing()
    {
        // Arrange
        await using RecorderFixture fixture = new(ImageId);
        fixture.Probe.Returning((Toolchain?)null);

        // Act
        await fixture.Recorder.TryRecordAsync(ImageId, TestContext.Current.CancellationToken);

        // Assert
        fixture.Broadcaster.SentNotifications.ShouldBeEmpty();
    }
}
