using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenRepositoryCoveredByCredential : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRepositoryCoveredByCredential()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedDetectedIssueAsync(MonitoredRepositoryId repositoryId)
    {
        // No POST endpoint exists for issues — they are created via integration events.
        // Seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .WithTitle("An issue")
            .WithLabels([])
            .Detected();

        dbContext.Set<Issue>().Add(issue);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<IssueSummary> GetSingleSummaryAsync(Guid repositoryId)
    {
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues?repositoryId={repositoryId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        PagedIssues? result = await response.Content.ReadFromJsonAsync<PagedIssues>(
            TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        return result.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenGitHubCredentialCoversRepository_SummaryCarriesGithubProviderType()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId);

        // RepositorySeeder uses slug "owner/repo" — seed that namespace so the credential covers it.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        await SeedDetectedIssueAsync(MonitoredRepositoryId.From(repositoryId));

        // Act
        IssueSummary summary = await GetSingleSummaryAsync(repositoryId);

        // Assert
        summary.ShouldSatisfyAllConditions(
            () => summary.RepositorySlug.ShouldBe("owner/repo"),
            () => summary.ProviderType.ShouldBe("github"));
    }

    [Fact]
    public async Task WhenGitLabCredentialCoversRepository_SummaryCarriesGitlabProviderType()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitLabAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/gitlab-repo");
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        await SeedDetectedIssueAsync(MonitoredRepositoryId.From(repositoryId));

        // Act
        IssueSummary summary = await GetSingleSummaryAsync(repositoryId);

        // Assert
        summary.ShouldSatisfyAllConditions(
            () => summary.RepositorySlug.ShouldBe("owner/gitlab-repo"),
            () => summary.ProviderType.ShouldBe("gitlab"));
    }

    [Fact]
    public async Task WhenNoCredentialCoversRepository_SummaryHasNullProviderType()
    {
        // Arrange — the account owns no namespace covering "owner/repo"
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId);
        await SeedDetectedIssueAsync(MonitoredRepositoryId.From(repositoryId));

        // Act
        IssueSummary summary = await GetSingleSummaryAsync(repositoryId);

        // Assert
        summary.ProviderType.ShouldBeNull();
    }
}
