using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
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

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

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

        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Completed Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: BaseTime);

        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            $"feat/{issueNumber}-fix",
            $"https://github.com/owner/repo/pull/{issueNumber}",
            BaseTime.AddHours(1));
        CompletedIssue completed = review.Complete(BaseTime.AddHours(2));

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
