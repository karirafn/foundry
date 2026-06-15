using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.DeleteRepositoryTests;

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
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId);

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RepositoryNoLongerAppearsInGetRepositories()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId);

        await _client.DeleteAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage getResponse = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await getResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        repositories.ShouldNotBeNull();
        repositories.ShouldNotContain(r => r.Id == repositoryId);
    }
}
