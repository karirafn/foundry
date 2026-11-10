using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class GetFullLogAsync
{
    [Fact]
    public async Task WhenLogIsLongerThanTheContainerOutputTail_ReturnsItWhole()
    {
        // Arrange
        string raw = "HEAD\n" + new string('x', 70_000) + "\nTAIL";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime().WithGetAllLogsResult(raw);
        DockerWorkerOrchestrator sut = new(runtime);

        // Act
        string? result = await sut.GetFullLogAsync("container-full", CancellationToken.None);

        // Assert
        result.ShouldBe(raw);
    }

    [Fact]
    public async Task WhenLogContainsASecret_RedactsIt()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithGetAllLogsResult("error: Authentication failed for token ghp_abc123DefXyz");
        DockerWorkerOrchestrator sut = new(runtime);

        // Act
        string? result = await sut.GetFullLogAsync("container-secret", CancellationToken.None);

        // Assert
        result.ShouldNotBeNull().ShouldNotContain("ghp_abc123DefXyz");
    }

    [Fact]
    public async Task WhenContainerIsGone_ReturnsNull()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime().WithGetAllLogsResult(null);
        DockerWorkerOrchestrator sut = new(runtime);

        // Act
        string? result = await sut.GetFullLogAsync("container-gone", CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }
}
