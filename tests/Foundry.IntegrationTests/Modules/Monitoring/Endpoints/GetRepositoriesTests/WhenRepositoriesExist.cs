using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetRepositoriesTests;

public sealed class WhenRepositoriesExist : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRepositoriesExist()
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
    public async Task ReturnsRepositoriesForAccount()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub");
        Guid otherAccountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Other GitHub");

        await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/repo-a");
        await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/repo-b");
        await RepositorySeeder.SeedRepositoryAsync(_factory, otherAccountId, slug: "other/repo-other");

        // No endpoint exposes namespace seeding directly — seed via DbContext to simulate resolver state.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, otherAccountId, "other");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        repositories.ShouldNotBeNull();
        repositories.Count.ShouldBe(2);
        repositories.ShouldAllBe(r => r.AccountId == accountId);
    }

    [Fact]
    public async Task ProjectsAllFieldsCorrectly()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub");
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            accountId,
            slug: "acme/awesome-repo",
            pollIntervalSeconds: 300);

        // No endpoint exposes namespace seeding directly — seed via DbContext to simulate resolver state.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "acme");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        repositories.ShouldNotBeNull();
        RepositorySummary repository = repositories.ShouldHaveSingleItem();
        repository.ShouldSatisfyAllConditions(
            () => repository.Id.ShouldBe(repositoryId),
            () => repository.Slug.ShouldBe("acme/awesome-repo"),
            () => repository.AccountId.ShouldBe(accountId),
            () => repository.AccountName.ShouldBe("My GitHub"),
            () => repository.ProviderType.ShouldBe("github"),
            () => repository.PollIntervalSeconds.ShouldBe(300),
            () => repository.IsActive.ShouldBeTrue());
    }

    [Fact]
    public async Task ProjectsProviderTypeForGitLabAccount()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitLabAccountAsync(_factory, name: "My GitLab");
        await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "group/repo");

        // No endpoint exposes namespace seeding directly — seed via DbContext to simulate resolver state.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "group");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        RepositorySummary repository = repositories.ShouldNotBeNull().ShouldHaveSingleItem();
        repository.ProviderType.ShouldBe("gitlab");
    }
}
