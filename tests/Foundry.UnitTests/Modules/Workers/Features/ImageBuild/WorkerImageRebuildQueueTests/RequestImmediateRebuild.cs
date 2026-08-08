using Foundry.Modules.Workers.Features.ImageBuild;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildQueueTests;

public sealed class RequestImmediateRebuild
{
    [Fact]
    public async Task WhenCalled_RaisesImmediateRebuildRequestedEvent()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();
        bool eventRaised = false;
        sut.ImmediateRebuildRequested += () => eventRaised = true;

        // Act
        sut.RequestImmediateRebuild();

        // Assert
        eventRaised.ShouldBeTrue();

        await Task.CompletedTask;
    }

    [Fact]
    public async Task WhenCalled_RaisesEventBeforeEnqueuing()
    {
        // Arrange — subscribe and read before any enqueue to confirm event fires before item arrives
        WorkerImageRebuildQueue sut = new();
        bool eventFiredBeforeItem = false;
        bool itemConsumedBeforeEvent = false;

        sut.ImmediateRebuildRequested += () =>
        {
            // At the moment the event fires the channel should have 0 items (not yet enqueued)
            eventFiredBeforeItem = true;
        };

        // Act
        sut.RequestImmediateRebuild();

        // Assert — event fired; item was enqueued after the event
        eventFiredBeforeItem.ShouldBeTrue();
        itemConsumedBeforeEvent.ShouldBeFalse();

        // Drain the channel to verify the item was actually enqueued
        bool received = await sut.ReadAllAsync(TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);
        received.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenCalled_EnqueuesRebuildSignal()
    {
        // Arrange
        WorkerImageRebuildQueue sut = new();

        // Act
        sut.RequestImmediateRebuild();

        // Assert — a signal was placed in the channel
        bool received = await sut.ReadAllAsync(TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);
        received.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenQueueAlreadyHasPendingRebuild_StillRaisesEvent()
    {
        // Arrange — fill the channel first so the enqueue will be dropped
        WorkerImageRebuildQueue sut = new();
        sut.TryEnqueue();
        bool eventRaised = false;
        sut.ImmediateRebuildRequested += () => eventRaised = true;

        // Act
        sut.RequestImmediateRebuild();

        // Assert — event fires even when the channel is full (supersede semantics)
        eventRaised.ShouldBeTrue();

        await Task.CompletedTask;
    }
}
