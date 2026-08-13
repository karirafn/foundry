using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Credentials.Features.CreditProbe;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.CheckCreditsNowTests;

/// <summary>
/// Verifies that the probe endpoint returns 202 Accepted with <c>inFlight: true</c>
/// when a probe is already running, so the client shows the in-flight state instead of
/// starting another.
/// Docker is not available in the sandbox — these tests run in CI where containers are available.
/// </summary>
public sealed class WhenProbeAlreadyRunning : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenProbeAlreadyRunning()
    {
        StubCreditProbeCoordinator coordinator = new(new CreditProbeResult.AlreadyRunning());

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<ICreditProbeCoordinator>();
            services.AddSingleton<ICreditProbeCoordinator>(coordinator);
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Returns202WithInFlightTrue()
    {
        // Arrange — coordinator is stubbed to return AlreadyRunning

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri("/api/credentials/probe", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        CheckCreditsNow.Response? body = await response.Content
            .ReadFromJsonAsync<CheckCreditsNow.Response>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.InFlight.ShouldBeTrue();
    }
}
