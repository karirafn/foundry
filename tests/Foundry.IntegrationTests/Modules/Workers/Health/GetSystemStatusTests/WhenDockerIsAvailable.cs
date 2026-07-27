using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Workers.Features.DockerAvailability;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Health.GetSystemStatusTests;

public sealed class WhenDockerIsAvailable : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenDockerIsAvailable()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            // Replace the singleton so tests control the known value without a Docker daemon.
            services.RemoveAll<DockerAvailabilityState>();
            services.RemoveAll<IDockerAvailabilityState>();
            services.RemoveAll<IDockerAvailabilityStateMutator>();

            DockerAvailabilityState state = new();
            state.Set(true);

            services.AddSingleton(state);
            services.AddSingleton<IDockerAvailabilityState>(state);
            services.AddSingleton<IDockerAvailabilityStateMutator>(state);
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenDockerIsAvailable_ReturnsDockerAvailableTrue()
    {
        // Arrange — factory seeds state with IsAvailable = true.

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/system/status", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        SystemStatus? status = await response.Content.ReadFromJsonAsync<SystemStatus>(
            TestContext.Current.CancellationToken);
        status.ShouldNotBeNull();
        status.DockerAvailable.ShouldBeTrue();
    }
}
