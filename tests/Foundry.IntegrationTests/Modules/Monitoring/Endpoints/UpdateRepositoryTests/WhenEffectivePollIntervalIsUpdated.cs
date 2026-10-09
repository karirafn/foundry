using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateRepositoryTests;

public sealed class WhenEffectivePollIntervalIsUpdated : IAsyncDisposable
{
    // The factory starts with no GlobalSettings row, so GetPollIntervalSecondsAsync falls back to
    // GlobalSettings.DefaultPollIntervalSeconds, which is 30.
    private const int GlobalDefaultPollIntervalSeconds = 30;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenEffectivePollIntervalIsUpdated()
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
    public async Task WhenIntervalIsCleared_PollIntervalIsDefaultBecomesTrue()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org Update Clear");
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            accountId,
            slug: "owner/repo-clear-interval",
            pollIntervalSeconds: 600);

        object body = new
        {
            pollIntervalSeconds = (int?)null,
            isActive = true,
            maxConcurrentWorkers = 1,
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
            () => repository.PollIntervalSeconds.ShouldBeNull(),
            () => repository.EffectivePollIntervalSeconds.ShouldBe(GlobalDefaultPollIntervalSeconds),
            () => repository.PollIntervalIsDefault.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenIntervalIsSet_PollIntervalIsDefaultBecomesFalse()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org Update Set");
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            accountId,
            slug: "owner/repo-set-interval");

        object body = new
        {
            pollIntervalSeconds = 450,
            isActive = true,
            maxConcurrentWorkers = 1,
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
            () => repository.PollIntervalSeconds.ShouldBe(450),
            () => repository.EffectivePollIntervalSeconds.ShouldBe(450),
            () => repository.PollIntervalIsDefault.ShouldBeFalse());
    }
}
