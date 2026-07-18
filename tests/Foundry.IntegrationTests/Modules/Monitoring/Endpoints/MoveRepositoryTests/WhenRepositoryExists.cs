using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.MoveRepositoryTests;

public sealed class WhenRepositoryExists : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRepositoryExists()
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
    public async Task ReturnsNoContent()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, "owner-a/repo");
        await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, "owner-b/repo");

        // Act
        HttpResponseMessage response = await _client.PatchAsJsonAsync(
            new Uri($"/api/repositories/{repositoryId}/position", UriKind.Relative),
            new { position = 1 },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PersistsNewPositionOrder()
    {
        // Arrange — seed two repos; repoA gets position 0, repoB gets position 1
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repoAId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, "owner-a/repo");
        Guid repoBId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, "owner-b/repo");

        // Namespace derivation (Step 7) is not yet implemented — seed namespaces directly.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner-a", "owner-b");

        // Act — move repoA from position 0 to position 1
        await _client.PatchAsJsonAsync(
            new Uri($"/api/repositories/{repoAId}/position", UriKind.Relative),
            new { position = 1 },
            TestContext.Current.CancellationToken);

        // Assert — GET the list and confirm repoB is now first, repoA is second
        HttpResponseMessage getResponse = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary> repositories = (await getResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        repositories.Count.ShouldBe(2);
        repositories[0].ShouldSatisfyAllConditions(
            () => repositories[0].Id.ShouldBe(repoBId),
            () => repositories[0].Position.ShouldBe(0));
        repositories[1].ShouldSatisfyAllConditions(
            () => repositories[1].Id.ShouldBe(repoAId),
            () => repositories[1].Position.ShouldBe(1));
    }
}
