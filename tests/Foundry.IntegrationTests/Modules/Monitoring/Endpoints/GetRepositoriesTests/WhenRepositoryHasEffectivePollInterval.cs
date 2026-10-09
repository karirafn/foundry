using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetRepositoriesTests;

public sealed class WhenRepositoryHasEffectivePollInterval : IAsyncDisposable
{
    // The factory starts with no GlobalSettings row, so GetPollIntervalSecondsAsync falls back to
    // GlobalSettings.DefaultPollIntervalSeconds, which is 30.
    private const int GlobalDefaultPollIntervalSeconds = 30;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRepositoryHasEffectivePollInterval()
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
    public async Task WhenRepoHasNoOwnInterval_ReturnsGlobalDefaultAndMarkedAsDefault()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org Poll Default");
        await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            accountId,
            slug: "owner/repo-no-interval",
            pollIntervalSeconds: null);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        RepositorySummary repo = repositories.ShouldNotBeNull().ShouldHaveSingleItem();
        repo.ShouldSatisfyAllConditions(
            () => repo.PollIntervalSeconds.ShouldBeNull(),
            () => repo.EffectivePollIntervalSeconds.ShouldBe(GlobalDefaultPollIntervalSeconds),
            () => repo.PollIntervalIsDefault.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenRepoHasOwnInterval_ReturnsOwnValueAndNotMarkedAsDefault()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org Poll Own");
        await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            accountId,
            slug: "owner/repo-own-interval",
            pollIntervalSeconds: 300);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        RepositorySummary repo = repositories.ShouldNotBeNull().ShouldHaveSingleItem();
        repo.ShouldSatisfyAllConditions(
            () => repo.PollIntervalSeconds.ShouldBe(300),
            () => repo.EffectivePollIntervalSeconds.ShouldBe(300),
            () => repo.PollIntervalIsDefault.ShouldBeFalse());
    }
}
