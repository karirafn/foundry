using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.RecheckRepositoryEligibilityTests;

public sealed class WhenNoNamespaceCoversRepository : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenNoNamespaceCoversRepository()
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
    public async Task WhenNoNamespaceCoversRepo_ReturnsIneligibleWithNoCredentialViolation()
    {
        // Arrange — credential has no namespaces configured, so resolver returns null for the repo
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "No Token Org", token: null);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/no-token-repo");

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}/recheck", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        RepositorySummary? repository = await response.Content
            .ReadFromJsonAsync<RepositorySummary>(TestContext.Current.CancellationToken);
        repository.ShouldNotBeNull();
        repository.Eligibility.ShouldNotBeNull();
        repository.Eligibility.ShouldSatisfyAllConditions(
            () => repository.Eligibility.Status.ShouldBe("ineligible"),
            () => repository.Eligibility.Violations.ShouldHaveSingleItem(),
            () => repository.Eligibility.Violations[0].Rule.ShouldBe("no-credential:owner"));
    }
}
