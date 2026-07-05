using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// Scriptable spy of <see cref="IContainerOperations"/> for unit-testing Docker container
/// interactions with zero Docker daemon.
/// <para>
/// Script return values and exceptions via the <c>With*</c> fluent methods.
/// Inspect captured call arguments via <c>Last*</c> properties.
/// Unsupported members throw <see cref="NotSupportedException"/> to fail loudly.
/// </para>
/// </summary>
internal sealed class SpyContainerOperations : IContainerOperations
{
    private string _createContainerId = "spy-container-id";
    private Exception? _createException;
    private Exception? _stopException;
    private Exception? _removeException;
    private ContainerInspectResponse? _inspectResponse;
    private Exception? _inspectException;
    private IList<ContainerListResponse> _listResponse = [];

    // Captured arguments
    public CreateContainerParameters? LastCreateParameters { get; private set; }
    public string? LastStopContainerId { get; private set; }
    public ContainerStopParameters? LastStopParameters { get; private set; }
    public string? LastRemoveContainerId { get; private set; }
    public ContainerRemoveParameters? LastRemoveParameters { get; private set; }
    public ContainersListParameters? LastListParameters { get; private set; }

    public SpyContainerOperations WithContainerId(string containerId)
    {
        _createContainerId = containerId;
        return this;
    }

    public SpyContainerOperations WithCreateThrowing(Exception exception)
    {
        _createException = exception;
        return this;
    }

    public SpyContainerOperations WithStopThrowing(Exception exception)
    {
        _stopException = exception;
        return this;
    }

    public SpyContainerOperations WithRemoveThrowing(Exception exception)
    {
        _removeException = exception;
        return this;
    }

    public SpyContainerOperations WithInspectResponse(ContainerInspectResponse response)
    {
        _inspectResponse = response;
        return this;
    }

    public SpyContainerOperations WithInspectThrowing(Exception exception)
    {
        _inspectException = exception;
        return this;
    }

    public SpyContainerOperations WithListResponse(IList<ContainerListResponse> response)
    {
        _listResponse = response;
        return this;
    }

    public Task<CreateContainerResponse> CreateContainerAsync(
        CreateContainerParameters parameters,
        CancellationToken cancellationToken)
    {
        LastCreateParameters = parameters;
        if (_createException is not null)
        {
            return Task.FromException<CreateContainerResponse>(_createException);
        }

        return Task.FromResult(new CreateContainerResponse { ID = _createContainerId });
    }

    public Task<bool> StartContainerAsync(
        string id,
        ContainerStartParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<bool> StopContainerAsync(
        string id,
        ContainerStopParameters parameters,
        CancellationToken cancellationToken)
    {
        LastStopContainerId = id;
        LastStopParameters = parameters;
        if (_stopException is not null)
        {
            return Task.FromException<bool>(_stopException);
        }

        return Task.FromResult(true);
    }

    public Task RemoveContainerAsync(
        string id,
        ContainerRemoveParameters parameters,
        CancellationToken cancellationToken)
    {
        LastRemoveContainerId = id;
        LastRemoveParameters = parameters;
        if (_removeException is not null)
        {
            return Task.FromException(_removeException);
        }

        return Task.CompletedTask;
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

    public Task<IList<ContainerListResponse>> ListContainersAsync(
        ContainersListParameters parameters,
        CancellationToken cancellationToken)
    {
        LastListParameters = parameters;
        return Task.FromResult(_listResponse);
    }

    public Task<MultiplexedStream> GetContainerLogsAsync(
        string id,
        bool tty,
        ContainerLogsParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(new MultiplexedStream(Stream.Null, false));

    public Task<MultiplexedStream> AttachContainerAsync(
        string id,
        bool tty,
        ContainerAttachParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(AttachContainerAsync)} is not exercised.");

    public Task<Stream> ExportContainerAsync(string id, CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(ExportContainerAsync)} is not exercised.");

    public Task ExtractArchiveToContainerAsync(
        string id,
        ContainerPathStatParameters parameters,
        Stream stream,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(ExtractArchiveToContainerAsync)} is not exercised.");

    public Task<GetArchiveFromContainerResponse> GetArchiveFromContainerAsync(
        string id,
        GetArchiveFromContainerParameters parameters,
        bool statOnly,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(GetArchiveFromContainerAsync)} is not exercised.");

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
        => throw new NotSupportedException($"{nameof(GetContainerStatsAsync)} is not exercised.");

    public Task GetContainerStatsAsync(
        string id,
        ContainerStatsParameters parameters,
        IProgress<ContainerStatsResponse> progress,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(GetContainerStatsAsync)} is not exercised.");

    public Task<IList<ContainerFileSystemChangeResponse>> InspectChangesAsync(
        string id,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(InspectChangesAsync)} is not exercised.");

    public Task KillContainerAsync(
        string id,
        ContainerKillParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(KillContainerAsync)} is not exercised.");

    public Task<ContainerProcessesResponse> ListProcessesAsync(
        string id,
        ContainerListProcessesParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(ListProcessesAsync)} is not exercised.");

    public Task PauseContainerAsync(string id, CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(PauseContainerAsync)} is not exercised.");

    public Task<ContainersPruneResponse> PruneContainersAsync(
        ContainersPruneParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(PruneContainersAsync)} is not exercised.");

    public Task RenameContainerAsync(
        string id,
        ContainerRenameParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(RenameContainerAsync)} is not exercised.");

    public Task ResizeContainerTtyAsync(
        string id,
        ContainerResizeParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(ResizeContainerTtyAsync)} is not exercised.");

    public Task RestartContainerAsync(
        string id,
        ContainerRestartParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(RestartContainerAsync)} is not exercised.");

    public Task UnpauseContainerAsync(string id, CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(UnpauseContainerAsync)} is not exercised.");

    public Task<ContainerUpdateResponse> UpdateContainerAsync(
        string id,
        ContainerUpdateParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(UpdateContainerAsync)} is not exercised.");

    public Task<ContainerWaitResponse> WaitContainerAsync(
        string id,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{nameof(WaitContainerAsync)} is not exercised.");

    // Helper: builds a DockerContainerNotFoundException (sealed, no public ctor; use a 404 response)
    public static DockerContainerNotFoundException ContainerNotFoundException() =>
        new(HttpStatusCode.NotFound, "No such container");
}
