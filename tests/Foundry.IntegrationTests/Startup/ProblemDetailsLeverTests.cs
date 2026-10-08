using System.Net;
using System.Net.Http.Headers;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Startup;

/// <summary>
/// Verifies that AddProblemDetails() and UseStatusCodePages() are registered,
/// so any error response with no explicit body (e.g. a framework 404 on an unmapped
/// route) answers with a conforming application/problem+json body.
/// </summary>
public sealed class ProblemDetailsLeverTests : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public ProblemDetailsLeverTests()
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
    public async Task WhenRequestTargetsUnmappedRoute_ResponseIsApplicationProblemJson()
    {
        // Arrange
        // /api/this-route-does-not-exist is guaranteed unmapped — no module registers it.

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/this-route-does-not-exist", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        MediaTypeHeaderValue contentType =
            response.Content.Headers.ContentType.ShouldNotBeNull();
        contentType.MediaType.ShouldBe("application/problem+json");
    }
}
