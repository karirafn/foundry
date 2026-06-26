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

public sealed class WhenFollowingResolvedCursor : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public WhenFollowingResolvedCursor()
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

    [Fact]
    public async Task NextPageHasNoOverlapOrGap()
    {
        // Arrange — seed 3 resolved issues
        await SeedCompletedIssueAsync(issueNumber: 1, detectedAt: BaseTime.AddHours(-2));
        await SeedCompletedIssueAsync(issueNumber: 2, detectedAt: BaseTime.AddHours(-1));
        await SeedCompletedIssueAsync(issueNumber: 3, detectedAt: BaseTime);

        // First page
        HttpResponseMessage firstResponse = await _client.GetAsync(
            new Uri("/api/issues?states=completed&limit=2", UriKind.Relative),
            TestContext.Current.CancellationToken);

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        PagedIssues? firstPage = await firstResponse.Content
            .ReadFromJsonAsync<PagedIssues>(TestContext.Current.CancellationToken);
        firstPage.ShouldNotBeNull();
        firstPage.NextCursor.ShouldNotBeNull();

        // Act — follow nextCursor
        HttpResponseMessage secondResponse = await _client.GetAsync(
            new Uri($"/api/issues?states=completed&limit=2&cursor={Uri.EscapeDataString(firstPage.NextCursor)}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        PagedIssues? secondPage = await secondResponse.Content
            .ReadFromJsonAsync<PagedIssues>(TestContext.Current.CancellationToken);
        secondPage.ShouldNotBeNull();
        secondPage.ShouldSatisfyAllConditions(
            () => secondPage.Items.ShouldHaveSingleItem(),
            () => secondPage.NextCursor.ShouldBeNull());

        // Verify no overlap and no gap — combined issue numbers should be 1, 2, 3
        IEnumerable<int> firstNumbers = firstPage.Items.Select(i => i.IssueNumber);
        IEnumerable<int> secondNumbers = secondPage.Items.Select(i => i.IssueNumber);
        firstNumbers.Intersect(secondNumbers).ShouldBeEmpty();
        firstNumbers.Union(secondNumbers).OrderBy(n => n).ShouldBe([1, 2, 3]);
    }
}
