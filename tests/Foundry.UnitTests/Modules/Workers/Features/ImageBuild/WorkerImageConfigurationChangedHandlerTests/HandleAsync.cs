using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Features.ImageBuild;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageConfigurationChangedHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenEventReceived_EnqueuesRebuildRequest()
    {
        // Arrange
        SpyWorkerImageRebuildQueue queue = new();
        WorkerImageConfigurationChangedHandler sut = new(queue);

        // Act
        await sut.HandleAsync(new WorkerImageConfigurationChanged(), TestContext.Current.CancellationToken);

        // Assert
        queue.EnqueueCalled.ShouldBeTrue();
    }

    private sealed class SpyWorkerImageRebuildQueue : IWorkerImageRebuildQueue
    {
        public bool EnqueueCalled { get; private set; }

        public bool TryEnqueue()
        {
            EnqueueCalled = true;
            return true;
        }

        public async IAsyncEnumerable<bool> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
