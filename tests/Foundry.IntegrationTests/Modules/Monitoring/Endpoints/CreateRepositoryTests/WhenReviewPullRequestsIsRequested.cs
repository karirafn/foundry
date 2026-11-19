using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateRepositoryTests;

public sealed class WhenReviewPullRequestsIsRequested : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenReviewPullRequestsIsRequested()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<RepositorySummary> PostAsync(Guid accountId, object body)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreateRepositoryResponse>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Repository;
    }

    private async Task<RepositorySummary> ReloadAsync(Guid accountId, Guid repositoryId)
    {
        // An update that omits the toggle reloads the stored row and leaves the toggle unchanged.
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}", UriKind.Relative),
            new { isActive = true, maxConcurrentWorkers = 1 },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RepositorySummary>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenOmitted_ReviewPullRequestsDefaultsToTrue()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub");

        // Act
        RepositorySummary repository = await PostAsync(accountId, new { slug = "owner/repo" });

        // Assert
        repository.ReviewPullRequests.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenFalse_StoredRepositoryReportsReviewPullRequestsFalse()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub");
        RepositorySummary created = await PostAsync(accountId, new { slug = "owner/repo", reviewPullRequests = false });

        // Act
        RepositorySummary repository = await ReloadAsync(accountId, created.Id);

        // Assert
        repository.ReviewPullRequests.ShouldBeFalse();
    }
}
