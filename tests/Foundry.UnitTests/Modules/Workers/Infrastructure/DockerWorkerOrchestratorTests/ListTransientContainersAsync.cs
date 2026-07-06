using Docker.DotNet.Models;

using Foundry.Modules.Credentials.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class ListTransientContainersAsync
{
    private const string TransientLabel = "foundry.transient";
    private const string ManagedLabel = "foundry.managed";
    private const string WorkerRunIdLabel = "foundry.worker-run-id";

    private static CredentialsOrchestrator BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime);

    [Fact]
    public async Task WhenNoTransientContainersExist_ReturnsEmptyList()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse([]);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        IReadOnlyList<string> result = await sut.ListTransientContainersAsync(CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTransientContainersExist_ReturnsTheirIds()
    {
        // Arrange
        IList<ContainerListResponse> containers =
        [
            new() { ID = "transient-1", Labels = new Dictionary<string, string> { [TransientLabel] = "true" } },
            new() { ID = "transient-2", Labels = new Dictionary<string, string> { [TransientLabel] = "true" } },
        ];
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse(containers);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        IReadOnlyList<string> result = await sut.ListTransientContainersAsync(CancellationToken.None);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain("transient-1");
        result.ShouldContain("transient-2");
    }

    [Fact]
    public async Task WhenCalled_FiltersContainersByTransientLabel()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse([]);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.ListTransientContainersAsync(CancellationToken.None);

        // Assert — filter must target foundry.transient, NOT foundry.managed or foundry.login
        ContainersListParameters capturedParams = runtime.LastListParameters.ShouldNotBeNull();
        capturedParams.Filters.ShouldContainKey("label");
        capturedParams.Filters["label"].ShouldContainKey(TransientLabel);
        capturedParams.Filters["label"].ShouldNotContainKey(ManagedLabel);
    }

    [Fact]
    public async Task WhenCalled_DoesNotMatchWorkerLabeledContainers()
    {
        // Arrange — a worker-only container carries foundry.managed + foundry.worker-run-id but NOT foundry.transient
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse([]);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.ListTransientContainersAsync(CancellationToken.None);

        // Assert — filter must not include the worker-run-id label key
        ContainersListParameters capturedParams = runtime.LastListParameters.ShouldNotBeNull();
        capturedParams.Filters["label"].ShouldNotContainKey(WorkerRunIdLabel);
    }

    [Fact]
    public async Task WhenCalled_IncludesStoppedContainers()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse([]);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.ListTransientContainersAsync(CancellationToken.None);

        // Assert
        ContainersListParameters capturedParams = runtime.LastListParameters.ShouldNotBeNull();
        capturedParams.All.ShouldBe(true);
    }
}
