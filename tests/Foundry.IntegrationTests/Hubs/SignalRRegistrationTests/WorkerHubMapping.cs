using System.Net;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Hubs.SignalRRegistrationTests;

public sealed class WorkerHubMapping : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WorkerHubMapping()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsSuccessOnNegotiate()
    {
        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri("/hubs/workers/negotiate?negotiateVersion=1", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
