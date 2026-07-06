using Foundry.Modules.Credentials.Features.Login;
using Foundry.UnitTests.Fakes.Credentials;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Login.TransientContainerReaperTests;

public sealed class TickAsync
{
    [Fact]
    public async Task WhenNoTransientContainersExist_DoesNotStopOrRemoveAnyContainers()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new();
        TransientContainerReaper sut = new(orchestrator, NullLogger<TransientContainerReaper>.Instance);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBe(0);
        orchestrator.RemoveContainerCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenTransientContainersExist_StopsAndRemovesEach()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new();
        orchestrator.WithOrphanedTransientContainers("transient-1", "transient-2");
        TransientContainerReaper sut = new(orchestrator, NullLogger<TransientContainerReaper>.Instance);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBe(2);
        orchestrator.RemoveContainerCallCount.ShouldBe(2);
    }

    [Fact]
    public async Task WhenTickThrows_DoesNotPropagateException()
    {
        // Arrange — orchestrator throws when listing transient containers
        FakeCredentialsOrchestrator orchestrator = new();
        orchestrator.WithListTransientThrows(new InvalidOperationException("Docker daemon unavailable"));
        TransientContainerReaper sut = new(orchestrator, NullLogger<TransientContainerReaper>.Instance);

        // Act + Assert — tick body swallows non-cancellation exceptions (host-safe)
        await Should.NotThrowAsync(() => sut.TickForTest(CancellationToken.None));
    }
}
