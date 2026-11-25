using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ImageBuild;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildQueueTests;

public sealed class ReadAllAsync
{
    [Fact]
    public async Task WhenSignalEnqueued_YieldsItem()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();
        sut.TryEnqueue(ImageRebuildOrigin.Requested);

        // Act
        ImageRebuildOrigin received = await sut.ReadAllAsync(TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);

        // Assert
        received.ShouldBe(ImageRebuildOrigin.Requested);
    }

    [Fact]
    public async Task WhenSignalConsumedAndNewSignalEnqueued_YieldsNewItem()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();
        sut.TryEnqueue(ImageRebuildOrigin.Requested);

        await sut.ReadAllAsync(TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);

        sut.TryEnqueue(ImageRebuildOrigin.Requested);

        // Act
        ImageRebuildOrigin received = await sut.ReadAllAsync(TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);

        // Assert
        received.ShouldBe(ImageRebuildOrigin.Requested);
    }

    [Fact]
    public async Task WhenCancelled_CompletesWithoutYieldingMore()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();
        CancellationTokenSource cts = new();

        List<ImageRebuildOrigin> received = [];

        // Act — enqueue one, start reading, then cancel mid-stream
        sut.TryEnqueue(ImageRebuildOrigin.Requested);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (ImageRebuildOrigin signal in sut.ReadAllAsync(cts.Token))
            {
                received.Add(signal);
            }
        });

        // Assert — cancellation propagated (received may have 0 or 1 items depending on timing)
        received.Count.ShouldBeLessThanOrEqualTo(1);
    }
}
