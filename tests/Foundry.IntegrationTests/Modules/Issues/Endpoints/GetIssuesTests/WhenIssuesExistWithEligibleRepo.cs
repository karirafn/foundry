using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenIssuesExistWithEligibleRepo : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("owner/repo")).Value;

    public WhenIssuesExistWithEligibleRepo()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<MonitoredRepositoryId> SeedEligibleRepositoryAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GitHubAccount account = GitHubAccount.Create("my-org", "TOKEN", new Uri("https://github.com"));
        dbContext.Set<Account>().Add(account);

        MonitoredRepository repo = MonitoredRepository.Create(ValidSlug, account.Id, "github.com", null);
        repo.SetEligibility(new RepositoryEligibility.Eligible());
        dbContext.Set<MonitoredRepository>().Add(repo);

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repo.Id;
    }

    private async Task SeedIssueAsync(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        dbContext.Set<Issue>().Add(issue);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenRepoIsEligible_SummaryHasEligibleStatus()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedEligibleRepositoryAsync();
        await SeedIssueAsync(repoId, issueNumber: 1);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<IssueSummary>? summaries = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<IssueSummary>>(TestContext.Current.CancellationToken);
        summaries.ShouldNotBeNull();
        IssueSummary summary = summaries.ShouldHaveSingleItem();
        summary.RepositoryEligibilityStatus.ShouldBe("eligible");
    }
}
