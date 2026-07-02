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

/// <summary>
/// Integration test proving the real EF Core + eligibility query wiring returns queued issues
/// in Dispatch Order: eligible-repo queued issues (ordered by TierRank, Position, DetectedAt, Id)
/// partition first, ineligible-repo queued issues partition after, non-queued active states last.
/// </summary>
public sealed class WhenQueuedIssuesRequested_OrderedByDispatchOrder : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly DateTimeOffset Now = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public WhenQueuedIssuesRequested_OrderedByDispatchOrder()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<MonitoredRepositoryId> SeedEligibleRepositoryAsync(string slug, int position)
    {
        // No POST endpoint for repositories — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GitHubAccount account = GitHubAccount.Create(
            "my-org",
            "TOKEN",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        dbContext.Set<Account>().Add(account);

        RepositorySlug repoSlug = RepositorySlug.Create(slug).ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(repoSlug, account.Id, "github.com", null, position);
        repo.SetEligibility(new RepositoryEligibility.Eligible());
        dbContext.Set<MonitoredRepository>().Add(repo);

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repo.Id;
    }

    private async Task<MonitoredRepositoryId> SeedIneligibleRepositoryAsync(string slug)
    {
        // No POST endpoint for repositories — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GitHubAccount account = GitHubAccount.Create(
            "my-org",
            "TOKEN",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        dbContext.Set<Account>().Add(account);

        RepositorySlug repoSlug = RepositorySlug.Create(slug).ValueOrThrow();
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.AllowDirectPushes()]);
        MonitoredRepository repo = MonitoredRepository.Create(repoSlug, account.Id, "github.com", null);
        repo.SetEligibility(ineligible);
        dbContext.Set<MonitoredRepository>().Add(repo);

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repo.Id;
    }

    private async Task SeedQueuedIssueAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset detectedAt)
    {
        // No POST endpoint for issues — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: detectedAt);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);

        dbContext.Set<Issue>().Add(queued);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedRevisionQueuedIssueAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset detectedAt)
    {
        // No POST endpoint for issues — seed directly via DbContext.
        // Walk the state machine to RevisionQueuedIssue: Detect → Queue → Claim → Review → Revise.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: detectedAt);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            $"feat/issue-{issueNumber}",
            $"https://github.com/owner/repo/pull/{issueNumber}",
            detectedAt.AddHours(1));
        RevisionQueuedIssue revisionQueued = review.Revise([new ReviewComment("Please revise.")]);

        dbContext.Set<Issue>().Add(revisionQueued);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedContinuationQueuedIssueAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset detectedAt)
    {
        // No POST endpoint for issues — seed directly via DbContext.
        // Walk the state machine to ContinuationQueuedIssue: Detect → Queue → Claim → ContinuableFailed → Retry.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: detectedAt);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue continuableFailed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            $"feat/issue-{issueNumber}",
            "Non-zero exit code: 1",
            "generic_failure",
            detectedAt.AddHours(1));
        ContinuationQueuedIssue continuationQueued = continuableFailed.Retry();

        dbContext.Set<Issue>().Add(continuationQueued);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedDetectedIssueAsync(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        // No POST endpoint for issues — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now.AddHours(-5));

        dbContext.Set<Issue>().Add(detected);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenQueuedIssuesAcrossMultipleTiersAndRepos_EligibleComeFirstInDispatchOrder()
    {
        // Arrange — two eligible repos with different positions, an ineligible repo, and a detected issue.
        // Repo A: position 2, repo B: position 1 (lower position = higher priority).
        MonitoredRepositoryId repoA = await SeedEligibleRepositoryAsync("owner/repo-a", position: 2);
        MonitoredRepositoryId repoB = await SeedEligibleRepositoryAsync("owner/repo-b", position: 1);
        MonitoredRepositoryId ineligibleRepo = await SeedIneligibleRepositoryAsync("owner/ineligible");

        // Issue 1: fresh QueuedIssue on repoA (position 2, tier 2)
        await SeedQueuedIssueAsync(repoA, issueNumber: 1, detectedAt: Now.AddHours(-3));

        // Issue 2: RevisionQueuedIssue on repoB (position 1, tier 0) — highest priority despite repoB's lower position
        await SeedRevisionQueuedIssueAsync(repoB, issueNumber: 2, detectedAt: Now.AddHours(-2));

        // Issue 3: ContinuationQueuedIssue on repoB (position 1, tier 1)
        await SeedContinuationQueuedIssueAsync(repoB, issueNumber: 3, detectedAt: Now.AddHours(-1));

        // Issue 4: QueuedIssue on ineligible repo — must appear after all eligible-repo queued issues
        await SeedQueuedIssueAsync(ineligibleRepo, issueNumber: 4, detectedAt: Now.AddHours(-10));

        // Issue 5: DetectedIssue on repoA — non-queued, must appear last
        await SeedDetectedIssueAsync(repoA, issueNumber: 5);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<IssueSummary>? summaries = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<IssueSummary>>(TestContext.Current.CancellationToken);
        summaries.ShouldNotBeNull();
        summaries.Count.ShouldBe(5);

        // Eligible queued first, ordered by Dispatch Order (TierRank → Position → DetectedAt → Id):
        //   [0] issue 2: revision_queued on repoB (tier 0, position 1) — wins on tier rank
        //   [1] issue 3: continuation_queued on repoB (tier 1, position 1) — tier 1 beats tier 2
        //   [2] issue 1: queued on repoA (tier 2, position 2) — tier 2, higher position
        summaries[0].ShouldSatisfyAllConditions(
            () => summaries[0].IssueNumber.ShouldBe(2),
            () => summaries[0].State.ShouldBe("revision_queued"),
            () => summaries[0].RepositoryEligibilityStatus.ShouldBe("eligible"));

        summaries[1].ShouldSatisfyAllConditions(
            () => summaries[1].IssueNumber.ShouldBe(3),
            () => summaries[1].State.ShouldBe("continuation_queued"),
            () => summaries[1].RepositoryEligibilityStatus.ShouldBe("eligible"));

        summaries[2].ShouldSatisfyAllConditions(
            () => summaries[2].IssueNumber.ShouldBe(1),
            () => summaries[2].State.ShouldBe("queued"),
            () => summaries[2].RepositoryEligibilityStatus.ShouldBe("eligible"));

        // Ineligible-repo queued last in the queued partition
        summaries[3].ShouldSatisfyAllConditions(
            () => summaries[3].IssueNumber.ShouldBe(4),
            () => summaries[3].State.ShouldBe("queued"),
            () => summaries[3].RepositoryEligibilityStatus.ShouldBe("ineligible"));

        // Non-queued active state last
        summaries[4].ShouldSatisfyAllConditions(
            () => summaries[4].IssueNumber.ShouldBe(5),
            () => summaries[4].State.ShouldBe("detected"));
    }

    [Fact]
    public async Task WhenQueuedIssuesSameTierOnDifferentEligibleRepos_LowerPositionComesFirst()
    {
        // Arrange — two eligible repos at position 1 and 2, both with fresh QueuedIssues at the same DetectedAt.
        MonitoredRepositoryId repoAtPosition2 = await SeedEligibleRepositoryAsync("owner/repo-pos2", position: 2);
        MonitoredRepositoryId repoAtPosition1 = await SeedEligibleRepositoryAsync("owner/repo-pos1", position: 1);

        // Both detected at the same time so position is the sole tie-breaker.
        await SeedQueuedIssueAsync(repoAtPosition2, issueNumber: 10, detectedAt: Now);
        await SeedQueuedIssueAsync(repoAtPosition1, issueNumber: 20, detectedAt: Now);

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

        // Issue on repo at position 1 (issue 20) dispatched before issue on repo at position 2 (issue 10).
        summaries[0].IssueNumber.ShouldBe(20);
        summaries[1].IssueNumber.ShouldBe(10);
    }
}
