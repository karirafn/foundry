using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ImageBuild;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildQueueTests;

public sealed class TryEnqueue
{
    [Fact]
    public void WhenQueueIsEmpty_ReturnsTrue()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();

        // Act
        bool result = sut.TryEnqueue(ImageRebuildOrigin.Requested);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenQueueAlreadyHasPendingRebuild_ReturnsFalse()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();
        sut.TryEnqueue(ImageRebuildOrigin.Requested);

        // Act
        bool result = sut.TryEnqueue(ImageRebuildOrigin.Requested);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenPreviousRebuildWasConsumed_AcceptsNextSignal()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();
        sut.TryEnqueue(ImageRebuildOrigin.Requested);

        await sut.ReadAllAsync(CancellationToken.None)
            .FirstAsync(TestContext.Current.CancellationToken);

        // Act
        bool result = sut.TryEnqueue(ImageRebuildOrigin.Requested);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenAScheduledRequestFollowsAPendingRequestedOne_MergesIntoTheRequestedOne()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();
        sut.TryEnqueue(ImageRebuildOrigin.Requested);

        // Act
        bool accepted = sut.TryEnqueue(ImageRebuildOrigin.Scheduled);

        // Assert
        accepted.ShouldBeFalse();
        (await sut.ReadAllAsync(TestContext.Current.CancellationToken).FirstAsync(TestContext.Current.CancellationToken))
            .ShouldBe(ImageRebuildOrigin.Requested);
    }

    [Fact]
    public async Task WhenARequestedRequestFollowsAPendingScheduledOne_TheMergedRebuildIsRequested()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();
        sut.TryEnqueue(ImageRebuildOrigin.Scheduled);

        // Act
        bool accepted = sut.TryEnqueue(ImageRebuildOrigin.Requested);

        // Assert
        accepted.ShouldBeFalse();
        (await sut.ReadAllAsync(TestContext.Current.CancellationToken).FirstAsync(TestContext.Current.CancellationToken))
            .ShouldBe(ImageRebuildOrigin.Requested);
    }

    [Fact]
    public async Task WhenOnlyAScheduledRequestIsPending_YieldsScheduled()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();

        // Act
        sut.TryEnqueue(ImageRebuildOrigin.Scheduled);

        // Assert
        (await sut.ReadAllAsync(TestContext.Current.CancellationToken).FirstAsync(TestContext.Current.CancellationToken))
            .ShouldBe(ImageRebuildOrigin.Scheduled);
    }

    [Fact]
    public async Task WhenAScheduledRequestMergedIntoAPendingOne_OnlyOneRebuildIsYielded()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();
        sut.TryEnqueue(ImageRebuildOrigin.Requested);
        sut.TryEnqueue(ImageRebuildOrigin.Scheduled);
        await sut.ReadAllAsync(TestContext.Current.CancellationToken).FirstAsync(TestContext.Current.CancellationToken);

        // Act
        bool accepted = sut.TryEnqueue(ImageRebuildOrigin.Scheduled);

        // Assert
        accepted.ShouldBeTrue("the merged request left nothing behind in the queue");
    }
}
