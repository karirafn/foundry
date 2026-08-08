using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Features.ImageBuild;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageConfigurationChangedHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenEventReceived_RequestsImmediateRebuild()
    {
        // Arrange
        SpyWorkerImageRebuildQueue queue = new();
        WorkerImageConfigurationChangedHandler sut = new(queue);

        // Act
        await sut.HandleAsync(new WorkerImageConfigurationChanged(), TestContext.Current.CancellationToken);

        // Assert
        queue.RequestImmediateRebuildCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenEventReceived_RaisesImmediateRebuildRequestedEvent()
    {
        // Arrange
        SpyWorkerImageRebuildQueue queue = new();
        WorkerImageConfigurationChangedHandler sut = new(queue);
        bool eventRaised = false;
        queue.ImmediateRebuildRequested += () => eventRaised = true;

        // Act
        await sut.HandleAsync(new WorkerImageConfigurationChanged(), TestContext.Current.CancellationToken);

        // Assert
        eventRaised.ShouldBeTrue();
    }

    private sealed class SpyWorkerImageRebuildQueue : IWorkerImageRebuildQueue
    {
        public bool RequestImmediateRebuildCalled { get; private set; }

        public event Action? ImmediateRebuildRequested;

        public void RequestImmediateRebuild()
        {
            RequestImmediateRebuildCalled = true;
            ImmediateRebuildRequested?.Invoke();
            TryEnqueue();
        }

        public bool TryEnqueue() => true;

        public async IAsyncEnumerable<bool> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
