using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateRepositoryTests;

public sealed class WhenEffectivePollIntervalIsRequested : IAsyncDisposable
{
    // The factory starts with no GlobalSettings row, so GetPollIntervalSecondsAsync falls back to
    // GlobalSettings.DefaultPollIntervalSeconds, which is 30.
    private const int GlobalDefaultPollIntervalSeconds = 30;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenEffectivePollIntervalIsRequested()
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
    public async Task WhenNoIntervalSupplied_EffectivePollIntervalIsGlobalDefaultAndMarkedAsDefault()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org Create Default");
        object body = new { slug = "owner/repo-default-interval" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        RepositorySummary? repository = await response.Content
            .ReadFromJsonAsync<RepositorySummary>(TestContext.Current.CancellationToken);
        repository.ShouldNotBeNull();
        repository.ShouldSatisfyAllConditions(
            () => repository.PollIntervalSeconds.ShouldBeNull(),
            () => repository.EffectivePollIntervalSeconds.ShouldBe(GlobalDefaultPollIntervalSeconds),
            () => repository.PollIntervalIsDefault.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenIntervalSupplied_EffectivePollIntervalIsOwnValueAndNotMarkedAsDefault()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org Create Own");
        object body = new { slug = "owner/repo-own-interval", pollIntervalSeconds = 120 };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        RepositorySummary? repository = await response.Content
            .ReadFromJsonAsync<RepositorySummary>(TestContext.Current.CancellationToken);
        repository.ShouldNotBeNull();
        repository.ShouldSatisfyAllConditions(
            () => repository.PollIntervalSeconds.ShouldBe(120),
            () => repository.EffectivePollIntervalSeconds.ShouldBe(120),
            () => repository.PollIntervalIsDefault.ShouldBeFalse());
    }
}
