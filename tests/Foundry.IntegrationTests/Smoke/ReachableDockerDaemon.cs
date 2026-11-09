using Docker.DotNet;
using Docker.DotNet.Models;

namespace Foundry.IntegrationTests.Smoke;

/// <summary>A Docker daemon that answers the health check's ping and nothing else.</summary>
internal sealed class ReachableDockerDaemon : ISystemOperations
{
    public Task PingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AuthenticateAsync(AuthConfig authConfig, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<VersionResponse> GetVersionAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<SystemInfoResponse> GetSystemInfoAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Stream> MonitorEventsAsync(ContainerEventsParameters parameters, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task MonitorEventsAsync(ContainerEventsParameters parameters, IProgress<Message> progress, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
