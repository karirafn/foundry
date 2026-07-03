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
