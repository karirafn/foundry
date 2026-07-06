using Docker.DotNet.Models;

using Foundry.Modules.Credentials.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Infrastructure.CredentialsOrchestratorTests;

public sealed class ListExitedTransientContainersAsync
{
    private const string TransientLabelFilter = "foundry.transient=true";

    private static CredentialsOrchestrator BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime);

    [Fact]
    public async Task WhenNoContainersExist_ReturnsEmptyList()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse([]);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        IReadOnlyList<string> result = await sut.ListExitedTransientContainersAsync(CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenExitedContainersExist_ReturnsTheirIds()
    {
        // Arrange
        IList<ContainerListResponse> containers =
        [
            new() { ID = "exited-1", State = "exited" },
            new() { ID = "exited-2", State = "dead" },
        ];
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse(containers);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        IReadOnlyList<string> result = await sut.ListExitedTransientContainersAsync(CancellationToken.None);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain("exited-1");
        result.ShouldContain("exited-2");
    }

    [Fact]
    public async Task WhenRunningContainerExists_ExcludesIt()
    {
        // Arrange — simulate the active login container
        IList<ContainerListResponse> containers =
        [
            new() { ID = "active-login", State = "running" },
            new() { ID = "exited-orphan", State = "exited" },
        ];
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse(containers);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        IReadOnlyList<string> result = await sut.ListExitedTransientContainersAsync(CancellationToken.None);

        // Assert
        result.ShouldNotContain("active-login");
        result.ShouldContain("exited-orphan");
    }

    [Fact]
    public async Task WhenOnlyRunningContainerExists_ReturnsEmptyList()
    {
        // Arrange
        IList<ContainerListResponse> containers =
        [
            new() { ID = "running-login", State = "running" },
        ];
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse(containers);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        IReadOnlyList<string> result = await sut.ListExitedTransientContainersAsync(CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenCalled_FiltersByTransientLabelKeyAndValue()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse([]);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.ListExitedTransientContainersAsync(CancellationToken.None);

        // Assert — filter key must be "foundry.transient=true" (key=value), not just "foundry.transient"
        ContainersListParameters capturedParams = runtime.LastListParameters.ShouldNotBeNull();
        capturedParams.Filters.ShouldContainKey("label");
        capturedParams.Filters["label"].ShouldContainKey(TransientLabelFilter);
    }

    [Fact]
    public async Task WhenCalled_IncludesStoppedContainers()
    {
        // Arrange — must use All=true to see exited containers (Docker omits them by default)
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse([]);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.ListExitedTransientContainersAsync(CancellationToken.None);

        // Assert
        ContainersListParameters capturedParams = runtime.LastListParameters.ShouldNotBeNull();
        capturedParams.All.ShouldBe(true);
    }
}
