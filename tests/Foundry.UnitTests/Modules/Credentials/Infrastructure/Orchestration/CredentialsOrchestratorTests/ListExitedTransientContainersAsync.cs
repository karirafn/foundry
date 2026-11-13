using Docker.DotNet.Models;

using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Infrastructure.Orchestration.CredentialsOrchestratorTests;

public sealed class ListExitedTransientContainersAsync
{
    private const string TransientLabelFilter = "foundry.transient=true";

    private static readonly DateTimeOffset Cutoff = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime YoungerThanCutoff = Cutoff.UtcDateTime.AddSeconds(1);
    private static readonly DateTime OlderThanCutoff = Cutoff.UtcDateTime.AddSeconds(-1);

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
        IReadOnlyList<string> result = await sut.ListExitedTransientContainersAsync(Cutoff, CancellationToken.None);

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
        IReadOnlyList<string> result = await sut.ListExitedTransientContainersAsync(Cutoff, CancellationToken.None);

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
        IReadOnlyList<string> result = await sut.ListExitedTransientContainersAsync(Cutoff, CancellationToken.None);

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
        IReadOnlyList<string> result = await sut.ListExitedTransientContainersAsync(Cutoff, CancellationToken.None);

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
        await sut.ListExitedTransientContainersAsync(Cutoff, CancellationToken.None);

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
        await sut.ListExitedTransientContainersAsync(Cutoff, CancellationToken.None);

        // Assert
        ContainersListParameters capturedParams = runtime.LastListParameters.ShouldNotBeNull();
        capturedParams.All.ShouldBe(true);
    }

    [Theory]
    [InlineData("created")]
    [InlineData("exited")]
    public async Task WhenNonRunningContainerWasCreatedAfterTheCutoff_ExcludesIt(string state)
    {
        // Arrange — a Publisher or helper its owner may still be starting or reading
        IList<ContainerListResponse> containers =
        [
            new() { ID = "young", State = state, Created = YoungerThanCutoff },
        ];
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse(containers);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        IReadOnlyList<string> result = await sut.ListExitedTransientContainersAsync(Cutoff, CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("created")]
    [InlineData("exited")]
    public async Task WhenNonRunningContainerWasCreatedBeforeTheCutoff_ReturnsIt(string state)
    {
        // Arrange — an orphan left by a Foundry crash, including one whose start call failed
        IList<ContainerListResponse> containers =
        [
            new() { ID = "old", State = state, Created = OlderThanCutoff },
        ];
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse(containers);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        IReadOnlyList<string> result = await sut.ListExitedTransientContainersAsync(Cutoff, CancellationToken.None);

        // Assert
        result.ShouldBe(["old"]);
    }

    [Fact]
    public async Task WhenRunningContainerWasCreatedBeforeTheCutoff_ExcludesIt()
    {
        // Arrange
        IList<ContainerListResponse> containers =
        [
            new() { ID = "old-running", State = "running", Created = OlderThanCutoff.AddDays(-1) },
        ];
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithListResponse(containers);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        IReadOnlyList<string> result = await sut.ListExitedTransientContainersAsync(Cutoff, CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }
}
