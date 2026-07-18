using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetRepositoriesTests;

public sealed class WhenRepositoriesHavePosition : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRepositoriesHavePosition()
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
    public async Task ReturnsRepositoriesOrderedByPosition()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Position Test Org");

        // Create three repositories — insertion order gives positions 0, 1, 2.
        // GET must return them in that order regardless of creation sequence.
        Guid repoAId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/repo-a");
        Guid repoBId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/repo-b");
        Guid repoCId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/repo-c");

        // No endpoint exposes namespace seeding directly — seed via DbContext to simulate resolver state.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        repositories.ShouldNotBeNull();
        repositories.Count.ShouldBe(3);
        repositories[0].ShouldSatisfyAllConditions(
            () => repositories[0].Id.ShouldBe(repoAId),
            () => repositories[0].Position.ShouldBe(0));
        repositories[1].ShouldSatisfyAllConditions(
            () => repositories[1].Id.ShouldBe(repoBId),
            () => repositories[1].Position.ShouldBe(1));
        repositories[2].ShouldSatisfyAllConditions(
            () => repositories[2].Id.ShouldBe(repoCId),
            () => repositories[2].Position.ShouldBe(2));
    }

    [Fact]
    public async Task ProjectsPositionFieldCorrectly()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Single Position Org");
        await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/single-repo");

        // No endpoint exposes namespace seeding directly — seed via DbContext to simulate resolver state.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        RepositorySummary repository = repositories.ShouldNotBeNull().ShouldHaveSingleItem();
        repository.Position.ShouldBe(0);
    }
}
