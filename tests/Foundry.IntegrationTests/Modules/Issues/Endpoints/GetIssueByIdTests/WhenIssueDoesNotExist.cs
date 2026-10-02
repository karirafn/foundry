using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssueByIdTests;

public sealed class WhenIssueDoesNotExist : IAsyncDisposable
{
    private const int NotFoundStatus = 404;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenIssueDoesNotExist()
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
        // Arrange
        Guid nonExistentId = Guid.NewGuid();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues/{nonExistentId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.ShouldSatisfyAllConditions(
            () => problem.Status.ShouldBe(NotFoundStatus),
            () => problem.Type.ShouldEndWith("Issue.NotFound"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }
}
