using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class StreamLogsAsync
{
    private static WorkerOptions DefaultOptions() => new()
    {
        Image = "test-image:latest",
        MemoryLimitMb = 512,
        CpuLimit = 1.5,
        PidsLimit = 256,
    };

    private static DockerWorkerOrchestrator BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime, Options.Create(DefaultOptions()));

    [Fact]
    public async Task WhenLineContainsHttpsUrlWithUserinfo_UserinfoRedacted()
    {
        // Arrange
        string line = "https://glpat-MySecretToken@gitlab.example.com/owner/repo.git";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithStreamLogLines(line);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        List<string> lines = [];
        await foreach (string emitted in sut.StreamLogsAsync("container-1", TestContext.Current.CancellationToken)
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            lines.Add(emitted);
        }

        // Assert
        lines.Count.ShouldBe(1);
        lines[0].ShouldNotContain("glpat-MySecretToken");
        lines[0].ShouldContain("https://***@gitlab.example.com");
    }

    [Fact]
    public async Task WhenLineContainsKnownTokenShape_TokenRedacted()
    {
        // Arrange
        string line = "error: Authentication failed for token ghp_abc123DefXyz";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithStreamLogLines(line);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        List<string> lines = [];
        await foreach (string emitted in sut.StreamLogsAsync("container-2", TestContext.Current.CancellationToken)
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            lines.Add(emitted);
        }

        // Assert
        lines.Count.ShouldBe(1);
        lines[0].ShouldNotContain("ghp_abc123DefXyz");
        lines[0].ShouldContain("***");
    }

    [Fact]
    public async Task WhenLineIsClean_PassesThroughUnchanged()
    {
        // Arrange
        string line = "Cloning into '/workspace'...";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithStreamLogLines(line);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        List<string> lines = [];
        await foreach (string emitted in sut.StreamLogsAsync("container-3", TestContext.Current.CancellationToken)
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            lines.Add(emitted);
        }

        // Assert
        lines.Count.ShouldBe(1);
        lines[0].ShouldBe(line);
    }
}
