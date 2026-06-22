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
        bool result = sut.TryEnqueue();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenQueueAlreadyHasPendingRebuild_ReturnsFalse()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();
        sut.TryEnqueue();

        // Act
        bool result = sut.TryEnqueue();

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenPreviousRebuildWasConsumed_AcceptsNextSignal()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();
        sut.TryEnqueue();

        await sut.ReadAllAsync(CancellationToken.None)
            .FirstAsync(TestContext.Current.CancellationToken);

        // Act
        bool result = sut.TryEnqueue();

        // Assert
        result.ShouldBeTrue();
    }
}
