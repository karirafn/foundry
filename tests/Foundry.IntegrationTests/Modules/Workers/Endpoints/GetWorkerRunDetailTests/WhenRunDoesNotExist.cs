using System.Net;

using Xunit;

using Shouldly;

namespace Foundry.IntegrationTests.Modules.Workers.Endpoints.GetWorkerRunDetailTests;

public sealed class WhenRunDoesNotExist : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRunDoesNotExist()
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
    public async Task WhenRunDoesNotExist_Returns404()
    {
        // Arrange
        Guid unknownId = Guid.NewGuid();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{unknownId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
