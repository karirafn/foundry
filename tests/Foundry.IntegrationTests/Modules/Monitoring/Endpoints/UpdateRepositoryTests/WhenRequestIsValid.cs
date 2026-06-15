using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateRepositoryTests;

public sealed class WhenRequestIsValid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRequestIsValid()
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
    public async Task ReturnsOkWithUpdatedRepository()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub");
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            accountId,
            slug: "owner/repo",
            pollIntervalSeconds: 300);

        object body = new
        {
            pollIntervalSeconds = 600,
            isActive = false,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        RepositorySummary? repository = await response.Content
            .ReadFromJsonAsync<RepositorySummary>(TestContext.Current.CancellationToken);
        repository.ShouldNotBeNull();
        repository.ShouldSatisfyAllConditions(
            () => repository.Id.ShouldBe(repositoryId),
            () => repository.AccountId.ShouldBe(accountId),
            () => repository.AccountName.ShouldBe("My GitHub"),
            () => repository.PollIntervalSeconds.ShouldBe(600),
            () => repository.IsActive.ShouldBeFalse());
    }
}
