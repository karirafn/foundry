using System.Net;
using System.Net.Http.Json;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateRepositoryTests;

public sealed class WhenAccountDoesNotExist : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountDoesNotExist()
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
        Guid validAccountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub");
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, validAccountId, slug: "owner/repo");
        Guid nonExistentAccountId = Guid.NewGuid();
        object body = new
        {
            pollIntervalSeconds = 600,
            isActive = true,
            maxConcurrentWorkers = 1,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{nonExistentAccountId}/repositories/{repositoryId}", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
