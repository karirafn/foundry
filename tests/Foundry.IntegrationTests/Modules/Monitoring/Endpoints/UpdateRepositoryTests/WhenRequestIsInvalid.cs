using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateRepositoryTests;

public sealed class WhenRequestIsInvalid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRequestIsInvalid()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
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
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/repo");
        object body = new { pollIntervalSeconds = 300, isActive = true, maxConcurrentWorkers };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}", UriKind.Relative),
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
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            accountId,
            slug: "owner/repo");
        object body = new { pollIntervalSeconds = 300, isActive = true, maxConcurrentWorkers };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert — aggregate unchanged means the repository retains its original maxConcurrentWorkers
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        HttpResponseMessage listResponse = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories =
            await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(
                TestContext.Current.CancellationToken);
        repositories.ShouldNotBeNull();
        RepositorySummary repository = repositories.ShouldHaveSingleItem();
        repository.Id.ShouldBe(repositoryId);
        repository.MaxConcurrentWorkers.ShouldBe(1);
    }
}
