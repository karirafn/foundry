using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenResolvedStatesRequested : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public WhenResolvedStatesRequested()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedCompletedIssueAsync(int issueNumber, DateTimeOffset detectedAt)
    {
        // No POST endpoint for issues — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            RepositoryId,
            issueNumber: issueNumber,
            title: $"Completed Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: detectedAt);

        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            $"feat/{issueNumber}-fix",
            $"https://github.com/owner/repo/pull/{issueNumber}",
            detectedAt.AddHours(1));
        CompletedIssue completed = review.Complete(detectedAt.AddHours(2));

        dbContext.Set<Issue>().Add(completed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedUnchangedIssueAsync(int issueNumber, DateTimeOffset detectedAt)
    {
        // No POST endpoint for issues — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            RepositoryId,
            issueNumber: issueNumber,
            title: $"Unchanged Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: detectedAt);

        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        UnchangedIssue unchanged = inProgress.MarkUnchanged(Guid.NewGuid());

        dbContext.Set<Issue>().Add(unchanged);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsPagedIssuesWithNextCursor_WhenMoreRowsExist()
    {
        // Arrange — seed 3 completed issues; request page of 2
        await SeedCompletedIssueAsync(issueNumber: 1, detectedAt: BaseTime.AddHours(-2));
        await SeedCompletedIssueAsync(issueNumber: 2, detectedAt: BaseTime.AddHours(-1));
        await SeedCompletedIssueAsync(issueNumber: 3, detectedAt: BaseTime);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues?states=completed&limit=2", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        PagedIssues? result = await response.Content
            .ReadFromJsonAsync<PagedIssues>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.ShouldSatisfyAllConditions(
            () => result.Items.Count.ShouldBe(2),
            () => result.NextCursor.ShouldNotBeNull());
    }

    [Fact]
    public async Task ReturnsItemsOrderedDetectedAtDescending()
    {
        // Arrange — seed 3 resolved issues with distinct timestamps
        await SeedCompletedIssueAsync(issueNumber: 1, detectedAt: BaseTime.AddHours(-2));
        await SeedCompletedIssueAsync(issueNumber: 2, detectedAt: BaseTime.AddHours(-1));
        await SeedCompletedIssueAsync(issueNumber: 3, detectedAt: BaseTime);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues?states=completed&limit=3", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        PagedIssues? result = await response.Content
            .ReadFromJsonAsync<PagedIssues>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Items.Count.ShouldBe(3);
        result.Items[0].IssueNumber.ShouldBe(3);
        result.Items[1].IssueNumber.ShouldBe(2);
        result.Items[2].IssueNumber.ShouldBe(1);
    }

    [Fact]
    public async Task LastPageHasNullNextCursor()
    {
        // Arrange
        await SeedCompletedIssueAsync(issueNumber: 1, detectedAt: BaseTime);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues?states=completed&limit=10", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        PagedIssues? result = await response.Content
            .ReadFromJsonAsync<PagedIssues>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.ShouldSatisfyAllConditions(
            () => result.Items.ShouldHaveSingleItem(),
            () => result.NextCursor.ShouldBeNull());
    }

    [Fact]
    public async Task MixingActiveAndResolvedStates_ReturnsBadRequest()
    {
        // Arrange — unchanged is active, completed is resolved; mixing them is rejected
        await SeedCompletedIssueAsync(issueNumber: 1, detectedAt: BaseTime);
        await SeedUnchangedIssueAsync(issueNumber: 2, detectedAt: BaseTime.AddHours(-1));

        // Act — comma-separated states mixing active (unchanged) and resolved (completed)
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues?states=completed,unchanged&limit=10", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
