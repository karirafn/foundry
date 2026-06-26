using System.Net;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenMalformedCursorProvided : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenMalformedCursorProvided()
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
    public async Task WhenResolvedStates_ReturnsBadRequest()
    {
        // Arrange — malformed cursor with resolved states
        const string MalformedCursor = "not-a-valid-cursor!!";

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues?states=completed&cursor={Uri.EscapeDataString(MalformedCursor)}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
