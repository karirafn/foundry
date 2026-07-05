using Docker.DotNet;
using Docker.DotNet.Models;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// Scriptable in-memory fake of <see cref="IContainerOperations"/> for unit-testing
/// Docker container interactions with zero Docker daemon.
/// <para>
/// Only the members exercised by <c>GetStatusAsync</c> are implemented with real behavior;
/// all other members throw <see cref="NotSupportedException"/> to fail loudly if called unexpectedly.
/// </para>
/// </summary>
internal sealed class FakeContainerOperations : IContainerOperations
{
    private ContainerInspectResponse? _inspectResponse;
    private Exception? _inspectException;

    /// <summary>Scripts <see cref="InspectContainerAsync"/> to return a running container response.</summary>
    public FakeContainerOperations WithRunningContainer()
    {
        _inspectResponse = new ContainerInspectResponse
        {
            State = new ContainerState { Running = true },
        };
        return this;
    }

    /// <summary>Scripts <see cref="InspectContainerAsync"/> to return an exited container response.</summary>
    public FakeContainerOperations WithExitedContainer(long exitCode = 0)
    {
        _inspectResponse = new ContainerInspectResponse
        {
            State = new ContainerState
            {
                Running = false,
                ExitCode = exitCode,
                FinishedAt = DateTimeOffset.UtcNow.ToString("O"),
            },
        };
        return this;
    }

    /// <summary>Scripts <see cref="InspectContainerAsync"/> to throw the given exception.</summary>
    public FakeContainerOperations WithInspectThrowing(Exception exception)
    {
        _inspectException = exception;
        return this;
    }

    public Task<ContainerInspectResponse> InspectContainerAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (_inspectException is not null)
        {
            return Task.FromException<ContainerInspectResponse>(_inspectException);
        }

        return Task.FromResult(_inspectResponse ?? new ContainerInspectResponse
        {
            State = new ContainerState { Running = true },
        });
    }

    public Task<CreateContainerResponse> CreateContainerAsync(
        CreateContainerParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<bool> StartContainerAsync(
        string id,
        ContainerStartParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<bool> StopContainerAsync(
        string id,
        ContainerStopParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task RemoveContainerAsync(
        string id,
        ContainerRemoveParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<IList<ContainerListResponse>> ListContainersAsync(
        ContainersListParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<MultiplexedStream> GetContainerLogsAsync(
        string id,
        bool tty,
        ContainerLogsParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<MultiplexedStream> AttachContainerAsync(
        string id,
        bool tty,
        ContainerAttachParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<Stream> ExportContainerAsync(string id, CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task ExtractArchiveToContainerAsync(
        string id,
        ContainerPathStatParameters parameters,
        Stream stream,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<GetArchiveFromContainerResponse> GetArchiveFromContainerAsync(
        string id,
        GetArchiveFromContainerParameters parameters,
        bool statOnly,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<Stream> GetContainerLogsAsync(
        string id,
        ContainerLogsParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task GetContainerLogsAsync(
        string id,
        ContainerLogsParameters parameters,
        CancellationToken cancellationToken,
        IProgress<string> progress)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<Stream> GetContainerStatsAsync(
        string id,
        ContainerStatsParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task GetContainerStatsAsync(
        string id,
        ContainerStatsParameters parameters,
        IProgress<ContainerStatsResponse> progress,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<IList<ContainerFileSystemChangeResponse>> InspectChangesAsync(
        string id,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task KillContainerAsync(
        string id,
        ContainerKillParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<ContainerProcessesResponse> ListProcessesAsync(
        string id,
        ContainerListProcessesParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task PauseContainerAsync(string id, CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<ContainersPruneResponse> PruneContainersAsync(
        ContainersPruneParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task RenameContainerAsync(
        string id,
        ContainerRenameParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task ResizeContainerTtyAsync(
        string id,
        ContainerResizeParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task RestartContainerAsync(
        string id,
        ContainerRestartParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task UnpauseContainerAsync(string id, CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<ContainerUpdateResponse> UpdateContainerAsync(
        string id,
        ContainerUpdateParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");

    public Task<ContainerWaitResponse> WaitContainerAsync(
        string id,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by GetStatusAsync.");
}
