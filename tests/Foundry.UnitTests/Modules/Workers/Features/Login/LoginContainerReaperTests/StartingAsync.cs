using Foundry.Modules.Credentials.Features.Login;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.LoginContainerReaperTests;

public sealed class StartingAsync
{
    [Fact]
    public async Task WhenNoOrphanedContainers_DoesNotStop_OrRemoveAnyContainers()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new();
        LoginContainerReaper sut = new(orchestrator, NullLogger<LoginContainerReaper>.Instance);

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBe(0);
        orchestrator.RemoveContainerCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenOneOrphanedContainer_StopsAndRemovesIt()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new();
        orchestrator.WithOrphanedLoginContainers("orphan-1");
        LoginContainerReaper sut = new(orchestrator, NullLogger<LoginContainerReaper>.Instance);

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBe(1);
        orchestrator.RemoveContainerCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenMultipleOrphanedContainers_StopsAndRemovesEach()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new();
        orchestrator.WithOrphanedLoginContainers("orphan-1", "orphan-2", "orphan-3");
        LoginContainerReaper sut = new(orchestrator, NullLogger<LoginContainerReaper>.Instance);

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBe(3);
        orchestrator.RemoveContainerCallCount.ShouldBe(3);
    }
}
