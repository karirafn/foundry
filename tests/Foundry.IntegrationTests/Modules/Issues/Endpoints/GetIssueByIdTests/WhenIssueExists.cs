using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
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

        DetectedIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId ?? RepositoryId)
            .WithIssueNumber(7)
            .WithTitle("A detected issue")
            .WithAuthor(IssueAuthor.Create("octocat").ValueOrThrow())
            .WithUrl(ProviderUrl.Create("https://github.com/owner/repo/issues/7").ValueOrThrow())
            .WithLabels(["bug"])
            .Detected();

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

        // RepositorySeeder uses slug "owner/repo" — seed that namespace so the resolver finds it.
        // No endpoint exposes namespace seeding directly; seed via DbContext to simulate resolver state.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

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

        // Seed namespace so the resolver can match "owner/gitlab-repo".
        // No endpoint exposes namespace seeding directly; seed via DbContext to simulate resolver state.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

        // No POST endpoint exists for issues — seed directly via DbContext.
        IssueId issueId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

            DetectedIssue issue = new IssueBuilder()
                .WithMonitoredRepositoryId(repoId)
                .WithIssueNumber(7)
                .WithTitle("A GitLab issue")
                .WithUrl(ProviderUrl.Create("https://gitlab.com/owner/gitlab-repo/issues/7").ValueOrThrow())
                .WithLabels([])
                .Detected();

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
