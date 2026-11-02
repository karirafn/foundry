using System.Net;
using System.Text.Json;

using Foundry.Modules.Credentials.Features.Usage;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.RefreshUsageTests;

/// <summary>
/// Verifies the 202 wire shape of <c>POST /api/credentials/usage/refresh</c> when a poll starts:
/// a camelCase <c>status</c> and no snapshot body (the result arrives via the SignalR push).
/// </summary>
public sealed class WhenPollStarted : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenPollStarted()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IUsagePollCoordinator>();
            services.AddSingleton<IUsagePollCoordinator>(new StubUsagePollCoordinator());
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private sealed class StubUsagePollCoordinator : IUsagePollCoordinator
    {
        public Task<UsageSnapshot?> PollAsync(CancellationToken cancellationToken)
            => Task.FromResult<UsageSnapshot?>(null);

        public Task<UsageRefreshOutcome> RequestRefreshAsync(CancellationToken cancellationToken)
            => Task.FromResult(UsageRefreshOutcome.Started);
    }

    [Fact]
    public async Task Returns202WithStartedStatusAndNoSnapshot()
    {
        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri("/api/credentials/usage/refresh", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        string json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("status").GetString().ShouldBe("started");
        doc.RootElement.TryGetProperty("limits", out _).ShouldBeFalse();
    }
}
