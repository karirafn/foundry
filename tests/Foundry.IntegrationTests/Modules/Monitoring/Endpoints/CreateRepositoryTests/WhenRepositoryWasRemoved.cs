using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateRepositoryTests;

public sealed class WhenRepositoryWasRemoved : IAsyncDisposable
{
    private const string Slug = "owner/repo";
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRepositoryWasRemoved()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedCompletedIssueAsync(Guid repositoryId, int issueNumber)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        CompletedIssue completed = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.From(repositoryId))
            .WithIssueNumber(issueNumber)
            .WithTitle($"Completed Issue {issueNumber}")
            .WithLabels([])
            .WithDetectedAt(BaseTime)
            .WithBranchName($"feat/{issueNumber}-fix")
            .WithPullRequestUrl($"https://github.com/owner/repo/pull/{issueNumber}")
            .WithFeedbackCutoffAt(BaseTime.AddHours(1))
            .WithCompletedAt(BaseTime.AddHours(2))
            .Completed();

        dbContext.Set<Issue>().Add(completed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task RemoveAsync(Guid accountId, Guid repositoryId)
    {
        HttpResponseMessage response = await _client.DeleteAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}", UriKind.Relative),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task<IssueSummary> GetSingleCompletedIssueAsync()
    {
        PagedIssues? page = await _client.GetFromJsonAsync<PagedIssues>(
            new Uri("/api/issues?states=completed", UriKind.Relative),
            TestContext.Current.CancellationToken);
        return page.ShouldNotBeNull().Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ReAddingRestoresTheSameRepository()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, Slug);
        await RemoveAsync(accountId, repositoryId);

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            new { slug = Slug, pollIntervalSeconds = 120 },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        CreateRepositoryResponse? created = await response.Content
            .ReadFromJsonAsync<CreateRepositoryResponse>(TestContext.Current.CancellationToken);
        created.ShouldNotBeNull().Repository.Id.ShouldBe(repositoryId);
        created.Repository.PollIntervalSeconds.ShouldBe(120);
    }

    [Fact]
    public async Task RemovingItAgainReturnsNotFound()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, Slug);
        await RemoveAsync(accountId, repositoryId);

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HistoryKeepsShowingTheSlugWhileRemoved()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, Slug);
        await SeedCompletedIssueAsync(repositoryId, issueNumber: 7);

        // Act
        await RemoveAsync(accountId, repositoryId);

        // Assert
        IssueSummary issue = await GetSingleCompletedIssueAsync();
        issue.RepositorySlug.ShouldBe(Slug);
        issue.RepositoryId.ShouldBe(repositoryId);
    }

    [Fact]
    public async Task HistoryShowsCompletedIssuesAttachedToTheRestoredRepository()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, Slug);
        await SeedCompletedIssueAsync(repositoryId, issueNumber: 7);
        await RemoveAsync(accountId, repositoryId);

        // Act
        Guid restoredId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, Slug);

        // Assert
        IssueSummary issue = await GetSingleCompletedIssueAsync();
        restoredId.ShouldBe(repositoryId);
        issue.RepositorySlug.ShouldBe(Slug);
        issue.RepositoryId.ShouldBe(repositoryId);
    }
}
