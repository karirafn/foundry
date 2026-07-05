using Docker.DotNet.Models;

using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class ListLoginContainersByLabelAsync
{
    private const string LoginLabel = "foundry.login";

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
    public async Task WhenNoLoginContainersExist_ReturnsEmptyList()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse([]);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        IReadOnlyList<ContainerId> result = await sut.ListLoginContainersByLabelAsync(CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenLoginContainersExist_ReturnsTheirIds()
    {
        // Arrange
        IList<ContainerListResponse> containers =
        [
            new() { ID = "login-container-1", Labels = new Dictionary<string, string> { [LoginLabel] = "true" } },
            new() { ID = "login-container-2", Labels = new Dictionary<string, string> { [LoginLabel] = "true" } },
        ];
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse(containers);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        IReadOnlyList<ContainerId> result = await sut.ListLoginContainersByLabelAsync(CancellationToken.None);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain(ContainerId.From("login-container-1"));
        result.ShouldContain(ContainerId.From("login-container-2"));
    }

    [Fact]
    public async Task WhenCalled_FiltersContainersByLoginLabel()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse([]);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.ListLoginContainersByLabelAsync(CancellationToken.None);

        // Assert — the filter must target the login label key
        ContainersListParameters capturedParams = runtime.LastListParameters.ShouldNotBeNull();
        capturedParams.Filters.ShouldContainKey("label");
        capturedParams.Filters["label"].ShouldContainKey(LoginLabel);
    }

    [Fact]
    public async Task WhenCalled_IncludesStoppedContainers()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse([]);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.ListLoginContainersByLabelAsync(CancellationToken.None);

        // Assert
        ContainersListParameters capturedParams = runtime.LastListParameters.ShouldNotBeNull();
        capturedParams.All.ShouldBe(true);
    }
}
