using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.GetCredentialsTests;

public sealed class WhenNoAccountExists : IAsyncDisposable
{
    private const int NotFoundStatus = 404;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenNoAccountExists()
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
    public async Task ReturnsNotFoundAsProblemDetails()
    {
        // Arrange — no ClaudeAccount row seeded; ClaudeAccountSeeder is removed in FoundryWebAppFactory

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/credentials", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.ShouldSatisfyAllConditions(
            () => problem.Status.ShouldBe(NotFoundStatus),
            () => problem.Type.ShouldEndWith("Credentials.NotFound"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }
}
