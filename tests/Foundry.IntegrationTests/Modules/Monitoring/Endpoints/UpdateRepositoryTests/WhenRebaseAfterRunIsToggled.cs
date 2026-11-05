using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateRepositoryTests;

public sealed class WhenRebaseAfterRunIsToggled : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRebaseAfterRunIsToggled()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<(Guid AccountId, Guid RepositoryId)> SeedAsync()
    {
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub");
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/repo");
        return (accountId, repositoryId);
    }

    private async Task<RepositorySummary> PutAsync(Guid accountId, Guid repositoryId, object body)
    {
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RepositorySummary>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenTurnedOff_RepositoryReportsRebaseAfterRunFalse()
    {
        // Arrange
        (Guid accountId, Guid repositoryId) = await SeedAsync();

        // Act
        RepositorySummary repository = await PutAsync(
            accountId,
            repositoryId,
            new { pollIntervalSeconds = 300, isActive = true, maxConcurrentWorkers = 1, rebaseAfterRun = false });

        // Assert
        repository.RebaseAfterRun.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenOmitted_RebaseAfterRunIsUnchanged()
    {
        // Arrange
        (Guid accountId, Guid repositoryId) = await SeedAsync();
        await PutAsync(
            accountId,
            repositoryId,
            new { pollIntervalSeconds = 300, isActive = true, maxConcurrentWorkers = 1, rebaseAfterRun = false });

        // Act
        RepositorySummary repository = await PutAsync(
            accountId,
            repositoryId,
            new { pollIntervalSeconds = 600, isActive = true, maxConcurrentWorkers = 1 });

        // Assert
        repository.RebaseAfterRun.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenNewlyAdded_RebaseAfterRunDefaultsToTrue()
    {
        // Arrange
        (Guid accountId, Guid repositoryId) = await SeedAsync();

        // Act
        RepositorySummary repository = await PutAsync(
            accountId,
            repositoryId,
            new { pollIntervalSeconds = 300, isActive = true, maxConcurrentWorkers = 1 });

        // Assert
        repository.RebaseAfterRun.ShouldBeTrue();
    }
}
