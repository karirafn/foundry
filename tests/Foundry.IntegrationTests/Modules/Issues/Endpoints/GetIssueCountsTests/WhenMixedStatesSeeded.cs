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

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssueCountsTests;

public sealed class WhenMixedStatesSeeded : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public WhenMixedStatesSeeded()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedDetectedIssueAsync(int issueNumber)
    {
        // No POST endpoint exists for issues — seeding directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue issue = DetectedIssue.Detect(
            RepositoryId,
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

    private async Task SeedCompletedIssueAsync(int issueNumber)
    {
        // No POST endpoint exists for issues — seeding directly through DbContext.
        // Walk the state machine chain to reach CompletedIssue.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            RepositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "feat/42-thing",
            "https://github.com/owner/repo/pull/1",
            DateTimeOffset.UtcNow.AddDays(1));
        CompletedIssue completed = review.Complete(DateTimeOffset.UtcNow);

        dbContext.Set<Issue>().Add(completed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedUnchangedIssueAsync(int issueNumber)
    {
        // No POST endpoint exists for issues — seeding directly through DbContext.
        // Walk the state machine chain to reach UnchangedIssue.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            RepositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        UnchangedIssue unchanged = inProgress.MarkUnchanged(Guid.NewGuid());

        dbContext.Set<Issue>().Add(unchanged);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsCountsForAllStates()
    {
        // Arrange — 3 detected, 2 completed, 1 unchanged
        await SeedDetectedIssueAsync(issueNumber: 1);
        await SeedDetectedIssueAsync(issueNumber: 2);
        await SeedDetectedIssueAsync(issueNumber: 3);
        await SeedCompletedIssueAsync(issueNumber: 4);
        await SeedCompletedIssueAsync(issueNumber: 5);
        await SeedUnchangedIssueAsync(issueNumber: 6);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues/counts", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IssueStateCounts? counts = await response.Content.ReadFromJsonAsync<IssueStateCounts>(
            TestContext.Current.CancellationToken);
        counts.ShouldNotBeNull();
        counts.ShouldSatisfyAllConditions(
            () => counts.Counts.Count.ShouldBe(13),
            () => counts.Counts["detected"].ShouldBe(3),
            () => counts.Counts["completed"].ShouldBe(2),
            () => counts.Counts["unchanged"].ShouldBe(1),
            () => counts.Counts["queued"].ShouldBe(0),
            () => counts.Counts["blocked"].ShouldBe(0),
            () => counts.Counts["in_progress"].ShouldBe(0),
            () => counts.Counts["review"].ShouldBe(0),
            () => counts.Counts["failed"].ShouldBe(0),
            () => counts.Counts["continuable_failed"].ShouldBe(0),
            () => counts.Counts["continuation_queued"].ShouldBe(0),
            () => counts.Counts["revision_queued"].ShouldBe(0),
            () => counts.Counts["revision_in_progress"].ShouldBe(0),
            () => counts.Counts["revision_failed"].ShouldBe(0));
    }
}
