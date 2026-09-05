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

public sealed class WhenResolvedFilteredByRepositoryId : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly DateTimeOffset BaseTime = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    public WhenResolvedFilteredByRepositoryId()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedCompletedIssueAsync(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        // No POST endpoint for issues — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        CompletedIssue completed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
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

    [Fact]
    public async Task ReturnsOnlyIssuesForSpecifiedRepository()
    {
        // Arrange — two resolved issues in different repos
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();

        await SeedCompletedIssueAsync(targetRepo, issueNumber: 1);
        await SeedCompletedIssueAsync(otherRepo, issueNumber: 2);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues?states=completed&repositoryId={targetRepo.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        PagedIssues? result = await response.Content
            .ReadFromJsonAsync<PagedIssues>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        IssueSummary item = result.Items.ShouldHaveSingleItem();
        item.IssueNumber.ShouldBe(1);
    }
}
