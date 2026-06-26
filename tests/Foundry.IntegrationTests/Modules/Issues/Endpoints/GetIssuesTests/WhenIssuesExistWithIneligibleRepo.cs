using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenIssuesExistWithIneligibleRepo : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public WhenIssuesExistWithIneligibleRepo()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<MonitoredRepositoryId> SeedRepositoryWithEligibilityAsync(RepositoryEligibility eligibility) =>
        await SeedRepositoryWithSlugAndEligibilityAsync("owner/repo", eligibility);

    private async Task<MonitoredRepositoryId> SeedRepositoryWithSlugAndEligibilityAsync(
        string slug,
        RepositoryEligibility eligibility)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GitHubAccount account = GitHubAccount.Create("my-org", "TOKEN", BaseUrl.Create("https://github.com").ValueOrThrow());
        dbContext.Set<Account>().Add(account);

        RepositorySlug repoSlug = RepositorySlug.Create(slug).ValueOrThrow();
        int position = await dbContext.Set<MonitoredRepository>().CountAsync(TestContext.Current.CancellationToken);
        MonitoredRepository repo = MonitoredRepository.Create(repoSlug, account.Id, "github.com", null, position);
        repo.SetEligibility(eligibility);
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
    public async Task WhenRepoIsIneligible_SummaryHasIneligibleStatus()
    {
        // Arrange
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.AllowDirectPushes()]);
        MonitoredRepositoryId repoId = await SeedRepositoryWithEligibilityAsync(ineligible);
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
        summary.RepositoryEligibilityStatus.ShouldBe("ineligible");
    }

    [Fact]
    public async Task WhenRepoIsUnreachable_SummaryHasUnreachableStatus()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepositoryWithEligibilityAsync(new RepositoryEligibility.Unreachable());
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
        summary.RepositoryEligibilityStatus.ShouldBe("unreachable");
    }

    [Fact]
    public async Task WhenRepoHasNoEligibilityEvaluated_SummaryHasUnreachableStatus()
    {
        // Arrange — repo seeded without setting eligibility; DB default is 'unreachable'
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GitHubAccount account = GitHubAccount.Create("my-org", "TOKEN", BaseUrl.Create("https://github.com").ValueOrThrow());
        dbContext.Set<Account>().Add(account);

        RepositorySlug slug = RepositorySlug.Create("owner/repo").ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(slug, account.Id, "github.com", null);
        dbContext.Set<MonitoredRepository>().Add(repo);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await SeedIssueAsync(repo.Id, issueNumber: 1);

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
        summary.RepositoryEligibilityStatus.ShouldBe("unreachable");
    }

    [Fact]
    public async Task WhenIssuesFromMixedEligibilityRepos_EachSummaryHasCorrectStatus()
    {
        // Arrange
        MonitoredRepositoryId eligibleRepoId = await SeedRepositoryWithSlugAndEligibilityAsync(
            "owner/eligible-repo",
            new RepositoryEligibility.Eligible());
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.AllowDirectPushes()]);
        MonitoredRepositoryId ineligibleRepoId = await SeedRepositoryWithSlugAndEligibilityAsync(
            "owner/ineligible-repo",
            ineligible);

        await SeedIssueAsync(eligibleRepoId, issueNumber: 1);
        await SeedIssueAsync(ineligibleRepoId, issueNumber: 2);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<IssueSummary>? summaries = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<IssueSummary>>(TestContext.Current.CancellationToken);
        summaries.ShouldNotBeNull();
        summaries.Count.ShouldBe(2);

        IssueSummary? issue1 = summaries.FirstOrDefault(s => s.IssueNumber == 1);
        IssueSummary? issue2 = summaries.FirstOrDefault(s => s.IssueNumber == 2);

        issue1.ShouldNotBeNull();
        issue2.ShouldNotBeNull();

        issue1.RepositoryEligibilityStatus.ShouldBe("eligible");
        issue2.RepositoryEligibilityStatus.ShouldBe("ineligible");
    }
}
