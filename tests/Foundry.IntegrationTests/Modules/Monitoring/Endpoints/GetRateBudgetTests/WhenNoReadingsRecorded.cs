using System.Net;
using System.Net.Http.Json;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetRateBudgetTests;

/// <summary>
/// Happy-path integration test: verifies the endpoint wires correctly and
/// emits all three budget entries even when no provider has been polled yet.
/// </summary>
public sealed class WhenNoReadingsRecorded : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenNoReadingsRecorded()
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
    public async Task ReturnsOkWithThreeBudgetEntries()
    {
        // Arrange — no readings recorded; the endpoint must still emit all three entries

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/rate-budget", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        RateBudgetSnapshotDto? body = await response.Content
            .ReadFromJsonAsync<RateBudgetSnapshotDto>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);

        body.ShouldNotBeNull();
        body.Budgets.Count.ShouldBe(3);

        body.Budgets[0].Budget.ShouldBe("GitHubRest");
        body.Budgets[1].Budget.ShouldBe("GitHubGraphQl");
        body.Budgets[2].Budget.ShouldBe("GitLabRest");
    }

    [Fact]
    public async Task WhenNoReadings_GitHubEntriesHaveUnknownHealth()
    {
        // Arrange — no readings; GitHub keys must surface as Unknown (fail-open)

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/rate-budget", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        RateBudgetSnapshotDto? body = await response.Content
            .ReadFromJsonAsync<RateBudgetSnapshotDto>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);

        body.ShouldNotBeNull();

        ProviderBudgetHeadroomDto gitHubRest = body.Budgets.Single(b => b.Budget == "GitHubRest");
        gitHubRest.Health.ShouldBe("Unknown");
        gitHubRest.Remaining.ShouldBeNull();

        ProviderBudgetHeadroomDto gitHubGraphQl = body.Budgets.Single(b => b.Budget == "GitHubGraphQl");
        gitHubGraphQl.Health.ShouldBe("Unknown");

        ProviderBudgetHeadroomDto gitLab = body.Budgets.Single(b => b.Budget == "GitLabRest");
        gitLab.Health.ShouldBeNull();
        gitLab.Floor.ShouldBeNull();
    }

    // ---- Local DTOs for deserialization ----

    private sealed record RateBudgetSnapshotDto(IReadOnlyList<ProviderBudgetHeadroomDto> Budgets);

    private sealed record ProviderBudgetHeadroomDto(
        string Budget,
        string DisplayName,
        int? Remaining,
        int? Limit,
        DateTimeOffset? ResetAt,
        DateTimeOffset? ObservedAt,
        int? Floor,
        string? Health);
}
