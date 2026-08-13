using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Credentials.Features.CreditProbe;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.CheckCreditsNowTests;

/// <summary>
/// Integration tests for POST /api/credentials/probe.
/// Docker is not available in the sandbox — these tests run in CI where containers are available.
/// The coordinator is replaced with a stub so no Docker dependency is required.
/// </summary>
public sealed class WhenProbeSucceeds : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenProbeSucceeds()
    {
        StubCreditProbeCoordinator coordinator = new(new CreditProbeResult.Restored());

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
    public async Task Returns200WithOutcomeAndNotInFlight()
    {
        // Arrange — coordinator is stubbed to return Restored

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri("/api/credentials/probe", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        CheckCreditsNow.Response? body = await response.Content
            .ReadFromJsonAsync<CheckCreditsNow.Response>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.ShouldSatisfyAllConditions(
            () => body.InFlight.ShouldBeFalse(),
            () => body.Outcome.ShouldBe("Restored"));
    }
}
