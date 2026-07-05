using Docker.DotNet;
using Docker.DotNet.Models;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// Abstract base that satisfies all <see cref="IContainerOperations"/> members with no-op
/// defaults so concrete test stubs only need to override the method under test.
/// </summary>
internal abstract class StubContainerOpsBase : IContainerOperations
{
    public abstract Task<MultiplexedStream> GetContainerLogsAsync(
        string id,
        bool tty,
        ContainerLogsParameters parameters,
        CancellationToken cancellationToken);

    public Task<CreateContainerResponse> CreateContainerAsync(
        CreateContainerParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(new CreateContainerResponse { ID = "stub-id" });

    public Task<bool> StartContainerAsync(
        string id,
        ContainerStartParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<bool> StopContainerAsync(
        string id,
        ContainerStopParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task RemoveContainerAsync(
        string id,
        ContainerRemoveParameters parameters,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<ContainerInspectResponse> InspectContainerAsync(
        string id,
        CancellationToken cancellationToken)
        => Task.FromResult(new ContainerInspectResponse());

    public Task<IList<ContainerListResponse>> ListContainersAsync(
        ContainersListParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult<IList<ContainerListResponse>>([]);

    public Task<MultiplexedStream> AttachContainerAsync(
        string id,
        bool tty,
        ContainerAttachParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(new MultiplexedStream(Stream.Null, false));

    public Task<Stream> ExportContainerAsync(string id, CancellationToken cancellationToken)
        => Task.FromResult<Stream>(Stream.Null);

    public Task ExtractArchiveToContainerAsync(
        string id,
        ContainerPathStatParameters parameters,
        Stream stream,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<GetArchiveFromContainerResponse> GetArchiveFromContainerAsync(
        string id,
        GetArchiveFromContainerParameters parameters,
        bool statOnly,
        CancellationToken cancellationToken)
        => Task.FromResult(new GetArchiveFromContainerResponse());

    public Task<Stream> GetContainerLogsAsync(
        string id,
        ContainerLogsParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult<Stream>(Stream.Null);

    public Task GetContainerLogsAsync(
        string id,
        ContainerLogsParameters parameters,
        CancellationToken cancellationToken,
        IProgress<string> progress)
        => Task.CompletedTask;

    public Task<Stream> GetContainerStatsAsync(
        string id,
        ContainerStatsParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult<Stream>(Stream.Null);

    public Task GetContainerStatsAsync(
        string id,
        ContainerStatsParameters parameters,
        IProgress<ContainerStatsResponse> progress,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<IList<ContainerFileSystemChangeResponse>> InspectChangesAsync(
        string id,
        CancellationToken cancellationToken)
        => Task.FromResult<IList<ContainerFileSystemChangeResponse>>([]);

    public Task KillContainerAsync(
        string id,
        ContainerKillParameters parameters,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<ContainerProcessesResponse> ListProcessesAsync(
        string id,
        ContainerListProcessesParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(new ContainerProcessesResponse());

    public Task PauseContainerAsync(string id, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<ContainersPruneResponse> PruneContainersAsync(
        ContainersPruneParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(new ContainersPruneResponse());

    public Task RenameContainerAsync(
        string id,
        ContainerRenameParameters parameters,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task ResizeContainerTtyAsync(
        string id,
        ContainerResizeParameters parameters,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task RestartContainerAsync(
        string id,
        ContainerRestartParameters parameters,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task UnpauseContainerAsync(string id, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<ContainerUpdateResponse> UpdateContainerAsync(
        string id,
        ContainerUpdateParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(new ContainerUpdateResponse());

    public Task<ContainerWaitResponse> WaitContainerAsync(
        string id,
        CancellationToken cancellationToken)
        => Task.FromResult(new ContainerWaitResponse());
}
