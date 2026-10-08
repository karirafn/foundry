using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
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

        // Use slug-derived name to avoid (base_url, name) unique constraint violations when
        // seeding multiple accounts with the same base URL.
        GitHubCredential credential = GitHubCredential.Create(
            slug.Replace("/", "-", StringComparison.Ordinal),
            "TOKEN",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        dbContext.Set<Credential>().Add(credential);

        RepositorySlug repoSlug = RepositorySlug.Create(slug).ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(repoSlug, "github.com", null, position).ValueOrThrow();
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

        // Use slug-derived name to avoid (base_url, name) unique constraint violations when
        // seeding multiple accounts with the same base URL.
        GitHubCredential credential = GitHubCredential.Create(
            slug.Replace("/", "-", StringComparison.Ordinal),
            "TOKEN",
            BaseUrl.Create("https://github.com").ValueOrThrow());
        dbContext.Set<Credential>().Add(credential);

        RepositorySlug repoSlug = RepositorySlug.Create(slug).ValueOrThrow();
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.AllowDirectPushes()]);
        MonitoredRepository repo = MonitoredRepository.Create(repoSlug, "github.com", null).ValueOrThrow();
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

        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithLabels([])
            .WithDetectedAt(detectedAt)
            .FreshQueued();

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

        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithLabels([])
            .WithDetectedAt(detectedAt)
            .WithBranchName($"feat/issue-{issueNumber}")
            .WithPullRequestUrl($"https://github.com/owner/repo/pull/{issueNumber}")
            .WithFeedbackCutoffAt(detectedAt.AddHours(1))
            .WithReviewComments([new ReviewComment("Please revise.")])
            .RevisionQueued();

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

        ContinuableFailedIssue continuableFailed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithLabels([])
            .WithDetectedAt(detectedAt)
            .WithBranchName($"feat/issue-{issueNumber}")
            .WithFailureReason("Non-zero exit code: 1")
            .WithFailureCategory(FailureCategory.NonZeroExit)
            .WithFailedAt(detectedAt.AddHours(1))
            .ContinuableFailed();

        ContinuationQueuedIssue continuationQueued = continuableFailed.Retry();

        dbContext.Set<Issue>().Add(continuationQueued);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedDetectedIssueAsync(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        // No POST endpoint for issues — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithLabels([])
            .WithDetectedAt(Now.AddHours(-5))
            .Detected();

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

        // Issue 1: fresh FreshQueuedIssue on repoA (position 2, tier 2)
        await SeedQueuedIssueAsync(repoA, issueNumber: 1, detectedAt: Now.AddHours(-3));

        // Issue 2: RevisionQueuedIssue on repoB (position 1, tier 0) — highest priority despite repoB's lower position
        await SeedRevisionQueuedIssueAsync(repoB, issueNumber: 2, detectedAt: Now.AddHours(-2));

        // Issue 3: ContinuationQueuedIssue on repoB (position 1, tier 1)
        await SeedContinuationQueuedIssueAsync(repoB, issueNumber: 3, detectedAt: Now.AddHours(-1));

        // Issue 4: FreshQueuedIssue on ineligible repo — must appear after all eligible-repo queued issues
        await SeedQueuedIssueAsync(ineligibleRepo, issueNumber: 4, detectedAt: Now.AddHours(-10));

        // Issue 5: DetectedIssue on repoA — non-queued, must appear last
        await SeedDetectedIssueAsync(repoA, issueNumber: 5);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        PagedIssues? result = await response.Content
            .ReadFromJsonAsync<PagedIssues>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Items.Count.ShouldBe(5);

        // Eligible queued are ordered by the capacity-aware multi-pass walk (CapacityAwareDispatchOrder.Order).
        // Each repo defaults to MaxConcurrentWorkers=1. No issues are in-flight, so headroom=1 per repo.
        //
        // DispatchOrderKey sort (TierRank → Position → DetectedAt → Id) gives this input order:
        //   issue 2 — revision_queued, repoB (tier 0, pos 1)
        //   issue 3 — continuation_queued, repoB (tier 1, pos 1)
        //   issue 1 — queued, repoA (tier 2, pos 2)
        //
        // Pass 1 walk (all headrooms = 1):
        //   issue 2 (repoB): placed[0]; repoB headroom → 0
        //   issue 3 (repoB): deferred — repoB saturated
        //   issue 1 (repoA): placed[1]; repoA headroom → 0
        // Pass 2: issue 3 (repoB, headroom=0) — all remaining repos saturated → stop
        // capacityDeferred = [issue 3], appended in key order after placed issues.
        //
        // Final eligible order: [issue 2, issue 1, issue 3]
        result.Items[0].ShouldSatisfyAllConditions(
            () => result.Items[0].IssueNumber.ShouldBe(2),
            () => result.Items[0].State.ShouldBe("revision_queued"),
            () => result.Items[0].RepositoryEligibilityStatus.ShouldBe("eligible"));

        result.Items[1].ShouldSatisfyAllConditions(
            () => result.Items[1].IssueNumber.ShouldBe(1),
            () => result.Items[1].State.ShouldBe("queued"),
            () => result.Items[1].RepositoryEligibilityStatus.ShouldBe("eligible"));

        // Issue 3 was deferred by the capacity walk (repoB already saturated in pass 1) and
        // is appended in key order after the placed issues — this is the intended Queue Position
        // semantics: it reflects the real claim order under the per-repo cap.
        result.Items[2].ShouldSatisfyAllConditions(
            () => result.Items[2].IssueNumber.ShouldBe(3),
            () => result.Items[2].State.ShouldBe("continuation_queued"),
            () => result.Items[2].RepositoryEligibilityStatus.ShouldBe("eligible"));

        // Ineligible-repo queued last in the queued partition
        result.Items[3].ShouldSatisfyAllConditions(
            () => result.Items[3].IssueNumber.ShouldBe(4),
            () => result.Items[3].State.ShouldBe("queued"),
            () => result.Items[3].RepositoryEligibilityStatus.ShouldBe("ineligible"));

        // Non-queued active state last
        result.Items[4].ShouldSatisfyAllConditions(
            () => result.Items[4].IssueNumber.ShouldBe(5),
            () => result.Items[4].State.ShouldBe("detected"));
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
        PagedIssues? result = await response.Content
            .ReadFromJsonAsync<PagedIssues>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Items.Count.ShouldBe(2);

        // Issue on repo at position 1 (issue 20) dispatched before issue on repo at position 2 (issue 10).
        result.Items[0].IssueNumber.ShouldBe(20);
        result.Items[1].IssueNumber.ShouldBe(10);
    }
}
