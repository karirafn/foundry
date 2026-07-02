using Docker.DotNet;
using Docker.DotNet.Models;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// No-op implementation of <see cref="IExecOperations"/> for tests that do not exercise exec behavior.
/// </summary>
internal sealed class NullExecOperations : IExecOperations
{
    public Task<ContainerExecCreateResponse> ExecCreateContainerAsync(
        string id,
        ContainerExecCreateParameters parameters,
        CancellationToken cancellationToken)
        => Task.FromResult(new ContainerExecCreateResponse { ID = "null-exec-id" });

    public Task<MultiplexedStream> StartAndAttachContainerExecAsync(
        string id,
        bool tty,
        CancellationToken cancellationToken)
        => Task.FromResult(new MultiplexedStream(Stream.Null, tty));

    public Task<ContainerExecInspectResponse> InspectContainerExecAsync(
        string id,
        CancellationToken cancellationToken)
        => Task.FromResult(new ContainerExecInspectResponse { ExecID = id, Running = false });

    public Task ResizeContainerExecTtyAsync(
        string id,
        ContainerResizeParameters parameters,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StartContainerExecAsync(string id, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<MultiplexedStream> StartWithConfigContainerExecAsync(
        string id,
        ContainerExecStartParameters eConfig,
        CancellationToken cancellationToken)
        => Task.FromResult(new MultiplexedStream(Stream.Null, false));
}

/// <summary>
/// Scriptable in-memory fake of <see cref="IExecOperations"/> for unit-testing
/// Docker exec interactions with zero Docker daemon.
/// <para>
/// Set <see cref="ExecStdout"/> to script what stdout the exec captures.
/// Inspect <see cref="LastCreateParameters"/> and <see cref="LastContainerId"/>
/// to assert the exec was constructed correctly.
/// </para>
/// </summary>
internal sealed class FakeExecOperations(string execStdout = "") : IExecOperations
{
    private const string FakeExecId = "fake-exec-id";

    public ContainerExecCreateParameters? LastCreateParameters { get; private set; }
    public string? LastContainerId { get; private set; }

    public string ExecStdout { get; set; } = execStdout;

    public Task<ContainerExecCreateResponse> ExecCreateContainerAsync(
        string id,
        ContainerExecCreateParameters parameters,
        CancellationToken cancellationToken)
    {
        LastContainerId = id;
        LastCreateParameters = parameters;
        return Task.FromResult(new ContainerExecCreateResponse { ID = FakeExecId });
    }

    public Task<MultiplexedStream> StartAndAttachContainerExecAsync(
        string id,
        bool tty,
        CancellationToken cancellationToken)
    {
        byte[] stdoutBytes = System.Text.Encoding.UTF8.GetBytes(ExecStdout);
        MemoryStream backing = new(stdoutBytes);
        return Task.FromResult(new MultiplexedStream(backing, tty));
    }

    public Task<ContainerExecInspectResponse> InspectContainerExecAsync(
        string id,
        CancellationToken cancellationToken)
        => Task.FromResult(new ContainerExecInspectResponse { ExecID = id, Running = false });

    public Task ResizeContainerExecTtyAsync(
        string id,
        ContainerResizeParameters parameters,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StartContainerExecAsync(string id, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<MultiplexedStream> StartWithConfigContainerExecAsync(
        string id,
        ContainerExecStartParameters eConfig,
        CancellationToken cancellationToken)
        => Task.FromResult(new MultiplexedStream(new MemoryStream(), false));

}
