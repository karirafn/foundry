using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateRepositoryTests;

public sealed class WhenRequestIsInvalid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly Guid _accountId;

    public WhenRequestIsInvalid()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
        _accountId = Guid.NewGuid();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task WhenMaxConcurrentWorkersIsOutOfRange_ReturnsBadRequest(int maxConcurrentWorkers)
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: $"My GitHub {maxConcurrentWorkers}");
        object body = new { slug = "owner/repo", maxConcurrentWorkers };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task WhenMaxConcurrentWorkersIsOutOfRange_AggregateUnchanged(int maxConcurrentWorkers)
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: $"My GitHub recheck {maxConcurrentWorkers}");
        object body = new { slug = "owner/repo", maxConcurrentWorkers };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert — aggregate unchanged means no repository was created
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        HttpResponseMessage listResponse = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories =
            await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(
                TestContext.Current.CancellationToken);
        repositories.ShouldNotBeNull();
        repositories.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenSlugIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        object body = new { slug = string.Empty };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{_accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenSlugIsWhitespace_ReturnsBadRequest()
    {
        // Arrange
        object body = new { slug = "   " };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{_accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenPollIntervalIsZero_ReturnsBadRequest()
    {
        // Arrange — poll-interval validation runs inside the handler after the account lookup,
        // so a real account must exist to reach it (a non-existent account returns 404 first).
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub poll-zero");
        object body = new { slug = "owner/repo", pollIntervalSeconds = 0 };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task WhenPollIntervalIsNegative_ReturnsBadRequest()
    {
        // Arrange — poll-interval validation runs inside the handler after the account lookup,
        // so a real account must exist to reach it (a non-existent account returns 404 first).
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub poll-negative");
        object body = new { slug = "owner/repo", pollIntervalSeconds = -1 };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }
}
