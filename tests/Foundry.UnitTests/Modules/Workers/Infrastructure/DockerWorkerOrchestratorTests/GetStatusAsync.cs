using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class GetStatusAsync
{
    private static WorkerOptions DefaultOptions() => new()
    {
        Image = "test-image:latest",
        MemoryLimitMb = 512,
        CpuLimit = 1.5,
        PidsLimit = 256,
    };

    private static DockerWorkerOrchestrator BuildSut(IContainerOperations containerOps) =>
        new(containerOps, new NullVolumeOperations(), new NullExecOperations(), Options.Create(DefaultOptions()));

    [Fact]
    public async Task WhenContainerIsRunning_ReturnsAvailableWithRunningStatus()
    {
        // Arrange
        FakeContainerOperations fake = new FakeContainerOperations().WithRunningContainer();
        DockerWorkerOrchestrator sut = BuildSut(fake);

        // Act
        WorkerStatusProbe result = await sut.GetStatusAsync("container-abc", CancellationToken.None);

        // Assert
        WorkerStatusProbe.Available available = result.ShouldBeOfType<WorkerStatusProbe.Available>();
        available.Status.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenContainerIsExited_ReturnsAvailableWithExitedStatus()
    {
        // Arrange
        FakeContainerOperations fake = new FakeContainerOperations().WithExitedContainer(exitCode: 1);
        DockerWorkerOrchestrator sut = BuildSut(fake);

        // Act
        WorkerStatusProbe result = await sut.GetStatusAsync("container-abc", CancellationToken.None);

        // Assert
        WorkerStatusProbe.Available available = result.ShouldBeOfType<WorkerStatusProbe.Available>();
        available.Status.ShouldSatisfyAllConditions(
            () => available.Status.IsRunning.ShouldBeFalse(),
            () => available.Status.ExitCode.ShouldBe(1));
    }

    [Fact]
    public async Task WhenContainerNotFound_ReturnsNotFound()
    {
        // Arrange
        FakeContainerOperations fake = new FakeContainerOperations()
            .WithInspectThrowing(new DockerContainerNotFoundException(HttpStatusCode.NotFound, "No such container"));
        DockerWorkerOrchestrator sut = BuildSut(fake);

        // Act
        WorkerStatusProbe result = await sut.GetStatusAsync("gone-container", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<WorkerStatusProbe.NotFound>();
    }

    [Fact]
    public async Task WhenHttpRequestExceptionThrown_ReturnsUnreachable()
    {
        // Arrange
        FakeContainerOperations fake = new FakeContainerOperations()
            .WithInspectThrowing(new HttpRequestException("Connection refused"));
        DockerWorkerOrchestrator sut = BuildSut(fake);

        // Act
        WorkerStatusProbe result = await sut.GetStatusAsync("container-abc", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<WorkerStatusProbe.Unreachable>();
    }

    [Fact]
    public async Task WhenTimeoutExceptionThrown_ReturnsUnreachable()
    {
        // Arrange
        FakeContainerOperations fake = new FakeContainerOperations()
            .WithInspectThrowing(new TimeoutException("Timed out"));
        DockerWorkerOrchestrator sut = BuildSut(fake);

        // Act
        WorkerStatusProbe result = await sut.GetStatusAsync("container-abc", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<WorkerStatusProbe.Unreachable>();
    }

    [Fact]
    public async Task WhenSelfTimeoutOperationCanceledThrown_ReturnsUnreachable()
    {
        // Arrange
        // A self-timeout OCE occurs when the internal timeout fires but the caller's token is NOT cancelled.
        FakeContainerOperations fake = new FakeContainerOperations()
            .WithInspectThrowing(new OperationCanceledException("Internal timeout"));
        DockerWorkerOrchestrator sut = BuildSut(fake);

        // Act — caller's token is not cancelled
        WorkerStatusProbe result = await sut.GetStatusAsync("container-abc", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<WorkerStatusProbe.Unreachable>();
    }

    [Fact]
    public async Task WhenCallerCancellationTokenCancelled_OperationCanceledExceptionPropagates()
    {
        // Arrange
        using CancellationTokenSource cts = new();
        FakeContainerOperations fake = new FakeContainerOperations()
            .WithInspectThrowing(new OperationCanceledException("Caller cancelled"));
        DockerWorkerOrchestrator sut = BuildSut(fake);
        await cts.CancelAsync();

        // Act & Assert — caller-cancellation OCE must propagate, not be swallowed as Unreachable
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await sut.GetStatusAsync("container-abc", cts.Token));
    }

    [Fact]
    public async Task WhenDockerApiExceptionNotContainerNotFound_PropagatesException()
    {
        // Arrange
        // A reachable-but-errored docker daemon (e.g. 500 Internal Server Error) must propagate,
        // not be classified as Unreachable.
        FakeContainerOperations fake = new FakeContainerOperations()
            .WithInspectThrowing(new DockerApiException(HttpStatusCode.InternalServerError, "Docker daemon error"));
        DockerWorkerOrchestrator sut = BuildSut(fake);

        // Act & Assert
        await Should.ThrowAsync<DockerApiException>(
            async () => await sut.GetStatusAsync("container-abc", CancellationToken.None));
    }

    private sealed class NullVolumeOperations : IVolumeOperations
    {
        public Task<VolumeResponse> CreateAsync(
            VolumesCreateParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new VolumeResponse { Name = parameters.Name });

        public Task<VolumeResponse> InspectAsync(string name, CancellationToken cancellationToken)
            => Task.FromResult(new VolumeResponse { Name = name });

        public Task<VolumesListResponse> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult(new VolumesListResponse());

        public Task<VolumesListResponse> ListAsync(
            VolumesListParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new VolumesListResponse());

        public Task<VolumesPruneResponse> PruneAsync(
            VolumesPruneParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new VolumesPruneResponse());

        public Task RemoveAsync(string name, bool? force, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
