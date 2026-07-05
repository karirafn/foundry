using Docker.DotNet.Models;

namespace Foundry.Shared.Infrastructure.Docker;

internal interface IDockerContainerRuntime
{
    // Container lifecycle
    Task<string> CreateAndStartAsync(
        CreateContainerParameters createParams,
        CancellationToken cancellationToken);

    Task StopAsync(
        string containerId,
        int waitBeforeKillSeconds,
        CancellationToken cancellationToken);

    Task RemoveAsync(
        string containerId,
        CancellationToken cancellationToken);

    Task<ContainerInspectResponse> InspectAsync(
        string containerId,
        CancellationToken cancellationToken);

    Task<IList<ContainerListResponse>> ListAsync(
        ContainersListParameters parameters,
        CancellationToken cancellationToken);

    // Logs
    IAsyncEnumerable<string> StreamLogsAsync(
        string containerId,
        CancellationToken cancellationToken);

    Task<string?> GetLogsAsync(
        string containerId,
        int tailLines,
        CancellationToken cancellationToken);

    // Volumes
    Task CreateVolumeAsync(
        VolumesCreateParameters parameters,
        CancellationToken cancellationToken);

    // Exec / file I/O
    Task<string> ExecCaptureStdoutAsync(
        string containerId,
        IReadOnlyList<string> command,
        IReadOnlyList<string> environment,
        int? maxBytes,
        CancellationToken cancellationToken);

    Task<string?> ReadFileAsync(
        string containerId,
        string filePath,
        CancellationToken cancellationToken);

    Task WriteFileAsync(
        string containerId,
        string filePath,
        string content,
        CancellationToken cancellationToken);
}
