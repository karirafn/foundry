using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateRepositoryTests;

public sealed class WhenSlugIsDuplicate : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenSlugIsDuplicate()
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
    public async Task ReturnsConflict()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub");
        object body = new { slug = "owner/repo" };

        await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Act — create a second repository with the same slug
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ReturnsProblemDetailsBody()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub Dup");
        string slug = "owner/repo-dup";
        string expectedDetail = $"A repository with slug '{slug}' already exists.";
        object body = new { slug };

        await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Act — create a second repository with the same slug
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        string responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(responseBody);
        JsonElement root = doc.RootElement;

        root.ShouldSatisfyAllConditions(
            () => root.GetProperty("type").GetString().ShouldEndWith("Repository.DuplicateSlug"),
            () => root.GetProperty("detail").GetString().ShouldBe(expectedDetail));
    }
}
