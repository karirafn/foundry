using Docker.DotNet;
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

    private static DockerWorkerOrchestrator BuildSut(SpyContainerOperations containerOps) =>
        new(containerOps, new NullVolumeOperations(), new NullExecOperations(), Options.Create(DefaultOptions()));

    [Fact]
    public async Task WhenNoLoginContainersExist_ReturnsEmptyList()
    {
        // Arrange
        SpyContainerOperations containerOps = new([]);
        DockerWorkerOrchestrator sut = BuildSut(containerOps);

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
        SpyContainerOperations containerOps = new(containers);
        DockerWorkerOrchestrator sut = BuildSut(containerOps);

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
        SpyContainerOperations containerOps = new([]);
        DockerWorkerOrchestrator sut = BuildSut(containerOps);

        // Act
        await sut.ListLoginContainersByLabelAsync(CancellationToken.None);

        // Assert — the filter must target the login label key
        ContainersListParameters capturedParams = containerOps.LastListParameters.ShouldNotBeNull();
        capturedParams.Filters.ShouldContainKey("label");
        capturedParams.Filters["label"].ShouldContainKey(LoginLabel);
    }

    [Fact]
    public async Task WhenCalled_IncludesStoppedContainers()
    {
        // Arrange
        SpyContainerOperations containerOps = new([]);
        DockerWorkerOrchestrator sut = BuildSut(containerOps);

        // Act
        await sut.ListLoginContainersByLabelAsync(CancellationToken.None);

        // Assert
        ContainersListParameters capturedParams = containerOps.LastListParameters.ShouldNotBeNull();
        capturedParams.All.ShouldBe(true);
    }

    private sealed class SpyContainerOperations(IList<ContainerListResponse> containers) : IContainerOperations
    {
        public ContainersListParameters? LastListParameters { get; private set; }

        public Task<IList<ContainerListResponse>> ListContainersAsync(
            ContainersListParameters parameters,
            CancellationToken cancellationToken)
        {
            LastListParameters = parameters;
            return Task.FromResult(containers);
        }

        public Task<CreateContainerResponse> CreateContainerAsync(
            CreateContainerParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new CreateContainerResponse { ID = "null-container" });

        public Task<bool> StartContainerAsync(string id, ContainerStartParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> StopContainerAsync(string id, ContainerStopParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task RemoveContainerAsync(string id, ContainerRemoveParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerInspectResponse> InspectContainerAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerInspectResponse());

        public Task<MultiplexedStream> GetContainerLogsAsync(string id, bool tty, ContainerLogsParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(Stream.Null, false));

        public Task<MultiplexedStream> AttachContainerAsync(string id, bool tty, ContainerAttachParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(Stream.Null, false));

        public Task<Stream> ExportContainerAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task ExtractArchiveToContainerAsync(string id, ContainerPathStatParameters parameters, Stream stream, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<GetArchiveFromContainerResponse> GetArchiveFromContainerAsync(string id, GetArchiveFromContainerParameters parameters, bool statOnly, CancellationToken cancellationToken)
            => Task.FromResult(new GetArchiveFromContainerResponse());

        public Task<Stream> GetContainerLogsAsync(string id, ContainerLogsParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task GetContainerLogsAsync(string id, ContainerLogsParameters parameters, CancellationToken cancellationToken, IProgress<string> progress)
            => Task.CompletedTask;

        public Task<Stream> GetContainerStatsAsync(string id, ContainerStatsParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task GetContainerStatsAsync(string id, ContainerStatsParameters parameters, IProgress<ContainerStatsResponse> progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IList<ContainerFileSystemChangeResponse>> InspectChangesAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult<IList<ContainerFileSystemChangeResponse>>([]);

        public Task KillContainerAsync(string id, ContainerKillParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerProcessesResponse> ListProcessesAsync(string id, ContainerListProcessesParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerProcessesResponse());

        public Task PauseContainerAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainersPruneResponse> PruneContainersAsync(ContainersPruneParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ContainersPruneResponse());

        public Task RenameContainerAsync(string id, ContainerRenameParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ResizeContainerTtyAsync(string id, ContainerResizeParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RestartContainerAsync(string id, ContainerRestartParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UnpauseContainerAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerUpdateResponse> UpdateContainerAsync(string id, ContainerUpdateParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerUpdateResponse());

        public Task<ContainerWaitResponse> WaitContainerAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerWaitResponse());
    }

    private sealed class NullVolumeOperations : IVolumeOperations
    {
        public Task<VolumeResponse> CreateAsync(VolumesCreateParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new VolumeResponse { Name = parameters.Name });

        public Task<VolumeResponse> InspectAsync(string name, CancellationToken cancellationToken)
            => Task.FromResult(new VolumeResponse { Name = name });

        public Task<VolumesListResponse> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult(new VolumesListResponse());

        public Task<VolumesListResponse> ListAsync(VolumesListParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new VolumesListResponse());

        public Task<VolumesPruneResponse> PruneAsync(VolumesPruneParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new VolumesPruneResponse());

        public Task RemoveAsync(string name, bool? force, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
