using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenMalformedCursorProvided : IAsyncDisposable
{
    private const int BadRequestStatus = 400;
    private const string MalformedCursor = "not-a-valid-cursor!!";

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
    public async Task WhenResolvedStates_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange — malformed cursor with resolved states

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues?states=completed&cursor={Uri.EscapeDataString(MalformedCursor)}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Status.ShouldBe(BadRequestStatus);
    }

    [Fact]
    public async Task WhenActiveStates_CursorIsIgnored_ReturnsOk()
    {
        // Arrange — malformed cursor on the active path; cursor is not consulted for active states

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues?states=detected&cursor={Uri.EscapeDataString(MalformedCursor)}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — cursor is silently ignored on the active path, 200 OK is returned
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WhenNoStates_CursorIsIgnored_ReturnsOk()
    {
        // Arrange — malformed cursor with no states (defaults to active path)

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues?cursor={Uri.EscapeDataString(MalformedCursor)}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — cursor is silently ignored on the active (default) path
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
