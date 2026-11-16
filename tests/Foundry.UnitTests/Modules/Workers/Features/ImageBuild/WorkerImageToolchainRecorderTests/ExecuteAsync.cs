using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ImageBuild;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageToolchainRecorderTests;

public sealed class ExecuteAsync
{
    private const string ImageId = "sha256:aaaa";
    private const string NextImageId = "sha256:bbbb";

    [Fact]
    public async Task WhenTheCurrentImageHasNoListAtStartup_ProbesAndStoresIt()
    {
        // Arrange
        await using RecorderFixture fixture = new(ImageId);
        fixture.Probe.Returning(RecorderFixture.DockerOnly());

        // Act
        await fixture.Recorder.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        await RecorderFixture.EventuallyAsync(async () => await fixture.FindAsync(ImageId) is not null);
        WorkerImageToolchain row = (await fixture.FindAsync(ImageId)).ShouldNotBeNull();
        row.Toolchain.Has("docker").ShouldBeTrue();
        fixture.Probe.ProbedImageIds.ShouldBe([ImageId]);
    }

    [Fact]
    public async Task WhenTheCurrentImageAlreadyHasAList_DoesNotProbe()
    {
        // Arrange
        await using RecorderFixture fixture = new(ImageId);
        await fixture.StoreAsync(ImageId, RecorderFixture.DockerOnly());

        // Act
        bool recorded = await fixture.Recorder.TryRecordAsync(ImageId, TestContext.Current.CancellationToken);

        // Assert
        recorded.ShouldBeTrue();
        fixture.Probe.ProbedImageIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheImageIdIsUnknown_DoesNotProbe()
    {
        // Arrange
        await using RecorderFixture fixture = new(imageId: null);

        // Act
        await fixture.Recorder.StartAsync(TestContext.Current.CancellationToken);
        fixture.Recorder.ImageChanged();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        fixture.Probe.ProbedImageIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheProbeFails_RetriesUntilAListIsStored()
    {
        // Arrange
        await using RecorderFixture fixture = new(ImageId);
        fixture.Probe.Returning(null, null, RecorderFixture.DockerOnly());

        // Act
        await fixture.Recorder.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        await RecorderFixture.EventuallyAsync(async () => await fixture.FindAsync(ImageId) is not null);
        fixture.Probe.ProbedImageIds.ShouldBe([ImageId, ImageId, ImageId]);
    }

    [Fact]
    public async Task WhenAListIsStored_StopsProbing()
    {
        // Arrange
        await using RecorderFixture fixture = new(ImageId);
        fixture.Probe.Returning(RecorderFixture.DockerOnly());
        await fixture.Recorder.StartAsync(TestContext.Current.CancellationToken);
        await RecorderFixture.EventuallyAsync(async () => await fixture.FindAsync(ImageId) is not null);

        // Act
        await Task.Delay(150, TestContext.Current.CancellationToken);

        // Assert
        fixture.Probe.ProbedImageIds.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenANewImageIsBuilt_ProbesTheNewImageAndKeepsTheOldRow()
    {
        // Arrange
        await using RecorderFixture fixture = new(ImageId);
        fixture.Probe.Returning(RecorderFixture.DockerOnly());
        await fixture.Recorder.StartAsync(TestContext.Current.CancellationToken);
        await RecorderFixture.EventuallyAsync(async () => await fixture.FindAsync(ImageId) is not null);

        // Act
        fixture.PointCatalogAt(NextImageId);
        fixture.Recorder.ImageChanged();

        // Assert
        await RecorderFixture.EventuallyAsync(async () => await fixture.FindAsync(NextImageId) is not null);
        (await fixture.FindAsync(ImageId)).ShouldNotBeNull();
        fixture.Probe.ProbedImageIds.ShouldBe([ImageId, NextImageId]);
    }

    [Fact]
    public async Task WhenANewImageIsBuiltWhileWaitingToRetry_ProbesTheNewImageAtOnce()
    {
        // Arrange — a backoff far longer than the test, so only the image change can wake the recorder.
        await using RecorderFixture fixture = new(ImageId, new ImageBuildRetryPolicy(TimeSpan.FromHours(1), TimeSpan.FromHours(1)));
        fixture.Probe.Returning((Toolchain?)null);
        await fixture.Recorder.StartAsync(TestContext.Current.CancellationToken);
        await RecorderFixture.EventuallyAsync(() => Task.FromResult(fixture.Probe.ProbedImageIds.Count > 0));

        // Act
        fixture.Probe.Returning(RecorderFixture.DockerOnly());
        fixture.PointCatalogAt(NextImageId);
        fixture.Recorder.ImageChanged();

        // Assert
        await RecorderFixture.EventuallyAsync(async () => await fixture.FindAsync(NextImageId) is not null);
        fixture.Probe.ProbedImageIds.ShouldBe([ImageId, NextImageId]);
    }
}
