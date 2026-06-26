using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenMixedStatesRequested : IAsyncDisposable
{
    private const int BadRequestStatus = 400;

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
    public async Task ReturnsBadRequestAsProblemDetails()
    {
        // Arrange — mix of an active state ("detected") and a resolved state ("completed")

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues?states=detected&states=completed", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Status.ShouldBe(BadRequestStatus);
    }
}
