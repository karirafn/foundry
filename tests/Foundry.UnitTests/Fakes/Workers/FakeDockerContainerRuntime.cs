using System.Runtime.CompilerServices;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// Scriptable in-memory fake of <see cref="IDockerContainerRuntime"/> for unit-testing
/// orchestrator behavior without a real Docker daemon.
/// <para>
/// Script return values and exceptions via the <c>With*</c> fluent methods.
/// Capture call arguments via <c>Last*</c> and <c>All*</c> properties.
/// </para>
/// </summary>
internal sealed class FakeDockerContainerRuntime : IDockerContainerRuntime
{
    private string _createAndStartContainerId = "fake-container-id";
    private Exception? _createAndStartException;
    private ContainerInspectResponse? _inspectResponse;
    private Exception? _inspectException;
    private IList<ContainerListResponse> _listResponse = [];
    private string? _getLogsResult;
    private string _execCaptureStdout = "";
    private string? _readFileResult;
    private string[] _streamLogLines = [];

    // Captured call arguments
    public CreateContainerParameters? LastCreateAndStartParameters { get; private set; }
    public IReadOnlyList<CreateContainerParameters> AllCreateAndStartParameters { get; } =
        new List<CreateContainerParameters>();

    public string? LastStopContainerId { get; private set; }
    public int LastStopWaitBeforeKillSeconds { get; private set; }
    public string? LastRemoveContainerId { get; private set; }
    public string? LastInspectContainerId { get; private set; }
    public ContainersListParameters? LastListParameters { get; private set; }
    public VolumesCreateParameters? LastCreateVolumeParameters { get; private set; }
    public string? LastExecContainerId { get; private set; }
    public IReadOnlyList<string>? LastExecCommand { get; private set; }
    public IReadOnlyList<string>? LastExecEnvironment { get; private set; }
    public int? LastExecMaxBytes { get; private set; }
    public string? LastReadFilePath { get; private set; }
    public string? LastWriteFilePath { get; private set; }
    public string? LastWriteFileContent { get; private set; }
    public string? LastExecAsyncContainerId { get; private set; }
    public IReadOnlyList<string>? LastExecAsyncCommand { get; private set; }
    public IReadOnlyList<string>? LastExecAsyncEnvironment { get; private set; }
    public string? LastStreamLogsContainerId { get; private set; }
    public string? LastGetLogsContainerId { get; private set; }
    public int LastGetLogsTailLines { get; private set; }

    // ── Scripting fluent API ──────────────────────────────────────────────────

    public FakeDockerContainerRuntime WithContainerId(string containerId)
    {
        _createAndStartContainerId = containerId;
        return this;
    }

    public FakeDockerContainerRuntime WithCreateAndStartThrowing(Exception exception)
    {
        _createAndStartException = exception;
        return this;
    }

    public FakeDockerContainerRuntime WithInspectResponse(ContainerInspectResponse response)
    {
        _inspectResponse = response;
        return this;
    }

    public FakeDockerContainerRuntime WithInspectThrowing(Exception exception)
    {
        _inspectException = exception;
        return this;
    }

    public FakeDockerContainerRuntime WithListResponse(IList<ContainerListResponse> response)
    {
        _listResponse = response;
        return this;
    }

    public FakeDockerContainerRuntime WithGetLogsResult(string? result)
    {
        _getLogsResult = result;
        return this;
    }

    public FakeDockerContainerRuntime WithExecCaptureStdout(string stdout)
    {
        _execCaptureStdout = stdout;
        return this;
    }

    public FakeDockerContainerRuntime WithReadFileResult(string? result)
    {
        _readFileResult = result;
        return this;
    }

    public FakeDockerContainerRuntime WithStreamLogLines(params string[] lines)
    {
        _streamLogLines = lines;
        return this;
    }

    // ── IDockerContainerRuntime implementation ────────────────────────────────

    public Task<string> CreateAndStartAsync(
        CreateContainerParameters createParams,
        CancellationToken cancellationToken)
    {
        ((List<CreateContainerParameters>)AllCreateAndStartParameters).Add(createParams);
        LastCreateAndStartParameters = createParams;

        if (_createAndStartException is not null)
        {
            return Task.FromException<string>(_createAndStartException);
        }

        return Task.FromResult(_createAndStartContainerId);
    }

    public Task StopAsync(
        string containerId,
        int waitBeforeKillSeconds,
        CancellationToken cancellationToken)
    {
        LastStopContainerId = containerId;
        LastStopWaitBeforeKillSeconds = waitBeforeKillSeconds;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        LastRemoveContainerId = containerId;
        return Task.CompletedTask;
    }

    public Task<ContainerInspectResponse> InspectAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        LastInspectContainerId = containerId;

        if (_inspectException is not null)
        {
            return Task.FromException<ContainerInspectResponse>(_inspectException);
        }

        return Task.FromResult(_inspectResponse ?? new ContainerInspectResponse
        {
            State = new ContainerState { Running = true },
        });
    }

    public Task<IList<ContainerListResponse>> ListAsync(
        ContainersListParameters parameters,
        CancellationToken cancellationToken)
    {
        LastListParameters = parameters;
        return Task.FromResult(_listResponse);
    }

    public async IAsyncEnumerable<string> StreamLogsAsync(
        string containerId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastStreamLogsContainerId = containerId;
        foreach (string line in _streamLogLines)
        {
            yield return line;
            await Task.Yield();
        }
    }

    public Task<string?> GetLogsAsync(
        string containerId,
        int tailLines,
        CancellationToken cancellationToken)
    {
        LastGetLogsContainerId = containerId;
        LastGetLogsTailLines = tailLines;
        return Task.FromResult(_getLogsResult);
    }

    public Task CreateVolumeAsync(
        VolumesCreateParameters parameters,
        CancellationToken cancellationToken)
    {
        LastCreateVolumeParameters = parameters;
        return Task.CompletedTask;
    }

    public Task<string> ExecCaptureStdoutAsync(
        string containerId,
        IReadOnlyList<string> command,
        IReadOnlyList<string> environment,
        int? maxBytes,
        CancellationToken cancellationToken)
    {
        LastExecContainerId = containerId;
        LastExecCommand = command;
        LastExecEnvironment = environment;
        LastExecMaxBytes = maxBytes;

        string result = maxBytes is null
            ? _execCaptureStdout
            : _execCaptureStdout.Length > maxBytes.Value
                ? _execCaptureStdout[..maxBytes.Value]
                : _execCaptureStdout;

        return Task.FromResult(result);
    }

    public Task<string?> ReadFileAsync(
        string containerId,
        string filePath,
        CancellationToken cancellationToken)
    {
        LastReadFilePath = filePath;
        return Task.FromResult(_readFileResult);
    }

    public Task WriteFileAsync(
        string containerId,
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        LastWriteFilePath = filePath;
        LastWriteFileContent = content;
        return Task.CompletedTask;
    }

    public Task ExecAsync(
        string containerId,
        IReadOnlyList<string> command,
        IReadOnlyList<string> environment,
        CancellationToken cancellationToken)
    {
        LastExecAsyncContainerId = containerId;
        LastExecAsyncCommand = command;
        LastExecAsyncEnvironment = environment;
        return Task.CompletedTask;
    }
}
