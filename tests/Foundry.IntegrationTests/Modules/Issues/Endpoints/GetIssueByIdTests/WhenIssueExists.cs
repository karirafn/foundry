using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssueByIdTests;

public sealed class WhenIssueExists : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/7")).Value;

    public WhenIssueExists()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        // FoundryWebAppFactory creates an isolated in-memory SQLite connection per instance.
        // Each test class instance gets its own factory, so no explicit DELETE cleanup is needed.
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<DetectedIssue> SeedDetectedIssueAsync(MonitoredRepositoryId? repositoryId = null)
    {
        // No POST endpoint exists for issues — they are created via integration events.
        // Seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId ?? RepositoryId,
            issueNumber: 7,
            title: "A detected issue",
            body: "Issue body text",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["bug"],
            detectedAt: DateTimeOffset.UtcNow);

        dbContext.Set<Issue>().Add(issue);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return issue;
    }

    [Fact]
    public async Task WhenIssueExists_ReturnsOkWithCoreFields()
    {
        // Arrange
        DetectedIssue issue = await SeedDetectedIssueAsync();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues/{issue.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IssueDetail? detail = await response.Content.ReadFromJsonAsync<IssueDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull();
        detail.ShouldSatisfyAllConditions(
            () => detail.Id.ShouldBe(issue.Id.Value),
            () => detail.IssueNumber.ShouldBe(7),
            () => detail.Title.ShouldBe("A detected issue"),
            () => detail.State.ShouldBe("detected"),
            () => detail.Author.ShouldBe("octocat"),
            () => detail.Labels.ShouldBe(["bug"]),
            () => detail.StateDetails.ShouldBeNull());
    }

    [Fact]
    public async Task WhenIssueLinkedToGitHubRepository_ReturnsGithubProviderType()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId);
        MonitoredRepositoryId repoId = MonitoredRepositoryId.From(repositoryId);
        DetectedIssue issue = await SeedDetectedIssueAsync(repoId);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues/{issue.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IssueDetail? detail = await response.Content.ReadFromJsonAsync<IssueDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull();
        detail.ProviderType.ShouldBe("github");
    }

    [Fact]
    public async Task WhenIssueLinkedToGitLabRepository_ReturnsGitlabProviderType()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitLabAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/gitlab-repo");
        MonitoredRepositoryId repoId = MonitoredRepositoryId.From(repositoryId);
        ProviderUrl gitlabUrl =
            ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://gitlab.com/owner/gitlab-repo/issues/7")).Value;

        // No POST endpoint exists for issues — seed directly via DbContext.
        IssueId issueId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            IssueAuthor author = ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

            DetectedIssue issue = DetectedIssue.Detect(
                repoId,
                issueNumber: 7,
                title: "A GitLab issue",
                body: "Issue body",
                author: author,
                url: gitlabUrl,
                labels: [],
                detectedAt: DateTimeOffset.UtcNow);

            dbContext.Set<Issue>().Add(issue);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
            issueId = issue.Id;
        }

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues/{issueId.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IssueDetail? detail = await response.Content.ReadFromJsonAsync<IssueDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull();
        detail.ProviderType.ShouldBe("gitlab");
    }
}
