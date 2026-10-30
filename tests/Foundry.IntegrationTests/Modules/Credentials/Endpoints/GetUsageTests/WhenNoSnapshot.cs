using System.Net;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.GetUsageTests;

/// <summary>
/// Integration tests for <c>GET /api/credentials/usage</c> when no snapshot exists.
/// </summary>
public sealed class WhenNoSnapshot : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenNoSnapshot()
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
    public async Task Returns204NoContent()
    {
        // Arrange — no snapshot seeded (state starts empty)

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/credentials/usage", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
