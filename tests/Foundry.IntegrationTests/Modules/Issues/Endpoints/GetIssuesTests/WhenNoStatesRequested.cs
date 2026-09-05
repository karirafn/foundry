using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenNoStatesRequested : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    public WhenNoStatesRequested()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedActiveIssueAsync(int issueNumber)
    {
        // No POST endpoint for issues — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Active Issue {issueNumber}")
            .WithLabels([])
            .Detected();

        dbContext.Set<Issue>().Add(issue);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedResolvedIssueAsync(int issueNumber)
    {
        // No POST endpoint for issues — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        CompletedIssue completed = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Resolved Issue {issueNumber}")
            .WithLabels([])
            .WithDetectedAt(DateTimeOffset.UtcNow.AddHours(-2))
            .WithBranchName("feat/1-fix")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/10")
            .WithFeedbackCutoffAt(DateTimeOffset.UtcNow.AddHours(-1))
            .Completed();

        dbContext.Set<Issue>().Add(completed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsActiveIssuesAndExcludesResolved()
    {
        // Arrange
        await SeedActiveIssueAsync(issueNumber: 1);
        await SeedResolvedIssueAsync(issueNumber: 2);

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
        summary.ShouldSatisfyAllConditions(
            () => summary.IssueNumber.ShouldBe(1),
            () => summary.State.ShouldBe("detected"));
    }

    [Fact]
    public async Task ReturnsBareList()
    {
        // Arrange
        await SeedActiveIssueAsync(issueNumber: 1);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<IssueSummary>? summaries = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<IssueSummary>>(TestContext.Current.CancellationToken);
        summaries.ShouldNotBeNull();
        summaries.ShouldHaveSingleItem();
    }
}
