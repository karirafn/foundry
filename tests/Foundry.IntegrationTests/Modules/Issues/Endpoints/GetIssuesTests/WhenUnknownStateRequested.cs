using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenUnknownStateRequested : IAsyncDisposable
{
    private const int BadRequestStatus = 400;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenUnknownStateRequested()
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
    public async Task ReturnsBadRequestAsProblemDetails()
    {
        // Arrange — request with a state name not in the registry

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues?states=bogus", UriKind.Relative),
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
    public async Task WhenManyOversizedTokens_ReturnsBoundedBadRequest()
    {
        // Arrange — send 10 unknown state tokens each exceeding the 64-char cap;
        // NormalizeStates drops them all, but the resulting empty-unknown list still
        // means no states match — verify the response remains bounded ProblemDetails.
        // We also include one short unknown token to trigger the InvalidStates path.
        string longToken = new string('x', 70);
        string query = string.Join("&", Enumerable.Range(0, 10).Select(i => $"states={longToken}{i}"))
            + "&states=unknown_short";

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues?{query}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — 400 ProblemDetails, not an amplified bare-string body
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Status.ShouldBe(BadRequestStatus);
        problem.Detail.ShouldNotBeNull();
    }
}
