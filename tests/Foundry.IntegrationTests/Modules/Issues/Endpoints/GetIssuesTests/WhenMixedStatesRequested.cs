using System.Net;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenMixedStatesRequested : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenMixedStatesRequested()
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
    public async Task ReturnsBadRequest()
    {
        // Arrange — mix of an active state ("detected") and a resolved state ("completed")

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues?states=detected&states=completed", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
