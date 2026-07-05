using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.ServiceDefaults.ReadyEndpointTests;

public sealed class WhenDaemonReachable : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenDaemonReachable()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<ISystemOperations>();
            services.AddSingleton<ISystemOperations>(new AlwaysHealthySystemOperations());
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReadyEndpointIsReachable()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/ready", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldNotBe(HttpStatusCode.NotFound,
            "the /ready endpoint must be mapped unconditionally, not only in Development");
    }

    [Fact]
    public async Task WhenHealthy_ReturnsOk()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/ready", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// In-process fake that completes <see cref="PingAsync"/> without touching a real Docker daemon.
    /// </summary>
    private sealed class AlwaysHealthySystemOperations : ISystemOperations
    {
        public Task PingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AuthenticateAsync(AuthConfig authConfig, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<VersionResponse> GetVersionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SystemInfoResponse> GetSystemInfoAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Stream> MonitorEventsAsync(
            ContainerEventsParameters parameters,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task MonitorEventsAsync(
            ContainerEventsParameters parameters,
            IProgress<Message> progress,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
