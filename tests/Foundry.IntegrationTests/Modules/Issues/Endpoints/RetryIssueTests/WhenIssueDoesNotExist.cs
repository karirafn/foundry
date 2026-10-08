using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.RetryIssueTests;

public sealed class WhenIssueDoesNotExist : IAsyncDisposable
{
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
    public async Task ReturnsNotFound()
    {
        // Arrange
        Guid nonExistentId = Guid.NewGuid();

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/issues/{nonExistentId}/retry", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        MediaTypeHeaderValue? contentType = response.Content.Headers.ContentType;
        contentType.ShouldNotBeNull();
        contentType.MediaType.ShouldBe("application/problem+json");

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonNode? node = JsonNode.Parse(body);
        node.ShouldNotBeNull();

        string? type = node["type"]?.GetValue<string>();
        type.ShouldNotBeNull();
        type.ShouldEndWith(IssueErrors.NotFoundCode);

        string? detail = node["detail"]?.GetValue<string>();
        detail.ShouldNotBeNull();
    }
}
