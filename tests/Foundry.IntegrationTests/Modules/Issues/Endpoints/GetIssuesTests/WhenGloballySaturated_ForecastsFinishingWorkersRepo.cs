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
/// Integration proof for the production scenario from issue #569.
///
/// Scenario (default global max = 1 — nothing seeded in Settings):
///   Repo A: eligible, Position 0 (higher priority), MaxConcurrentWorkers = 1,
///           one in-flight InProgressIssue AND a tier-0 RevisionQueuedIssue.
///   Repo B: eligible, Position 1 (lower priority), idle,
///           one tier-2 FreshQueuedIssue.
///
/// Under global saturation (total in-flight = 1 >= globalMax = 1):
///   ForClaim gives repo A headroom 0 → its queued issue is deferred → repo B leads (WRONG, pre-fix).
///   ForForecast gives repo A headroom 1 (one worker assumed to finish) → repo A's tier-0 issue leads (CORRECT).
///
/// This test MUST fail against pre-step-4 code (where ForClaim was used for the dashboard) and
/// MUST pass with ForForecast wired in. The ordering assertion is the false-green litmus:
/// a 200 OK with the wrong order fails the test.
///
/// Docker is NOT available in this sandbox — compile-verified only; executes in CI.
/// </summary>
public sealed class WhenGloballySaturated_ForecastsFinishingWorkersRepo : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    public WhenGloballySaturated_ForecastsFinishingWorkersRepo()
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
        // Use slug-derived name to avoid (base_url, name) unique constraint violations when
        // seeding multiple accounts with the same base URL.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

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

    private async Task SeedInProgressIssueAsync(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        // No POST endpoint for issues — seed directly via DbContext.
        // InProgressIssue is the in-flight state counted by InFlightWorkerCountQuery.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithLabels([])
            .WithDetectedAt(Now.AddHours(-4))
            .InProgress();

        dbContext.Set<Issue>().Add(inProgress);
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

    [Fact]
    public async Task WhenRepoARunningAndBIdle_RepoAQueuedIssuePrecedesRepoBIssue()
    {
        // Arrange
        //
        // Repo A: Position 0 (higher priority), MaxConcurrentWorkers = 1.
        // Mirrors akraborg (Position 0) running issue #1148 with issue #950 queued.
        MonitoredRepositoryId repoA = await SeedEligibleRepositoryAsync("owner/repo-a", position: 0);

        // The in-flight issue saturates repo A under ForClaim (headroom = max(0, 1-1) = 0).
        // Under ForForecast (global saturated: totalInFlight=1 >= globalMax=1), headroom = max(0, 1-(1-1)) = 1.
        await SeedInProgressIssueAsync(repoA, issueNumber: 1000);

        // Repo A's queued issue: tier 0 (revision_queued) — highest dispatch priority.
        await SeedRevisionQueuedIssueAsync(repoA, issueNumber: 950, detectedAt: Now.AddHours(-3));

        // Repo B: Position 1 (lower priority), idle.
        // Mirrors roslyn-query (Position 11) with issue #86 queued.
        MonitoredRepositoryId repoB = await SeedEligibleRepositoryAsync("owner/repo-b", position: 1);

        // Repo B's queued issue: tier 2 (fresh queued) — lower dispatch priority than revision_queued.
        await SeedQueuedIssueAsync(repoB, issueNumber: 86, detectedAt: Now.AddHours(-2));

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        PagedIssues? result = await response.Content
            .ReadFromJsonAsync<PagedIssues>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();

        // The response must contain the two queued issues plus the in-flight issue (3 active issues total).
        // We assert ordering of the two queued issues specifically — that is the regression proof.
        // A response with the wrong order (repo B issue first) must fail this test.
        result.Items.Count.ShouldBe(3);

        // Issue 950 (repo A, revision_queued, tier 0) must precede issue 86 (repo B, fresh queued, tier 2).
        //
        // DispatchOrderKey sort (TierRank → Position → DetectedAt → Id) gives this input order for queued issues:
        //   issue 950 — revision_queued (tier 0), repoA (pos 0)
        //   issue 86  — queued (tier 2), repoB (pos 1)
        //
        // With ForForecast: repo A headroom = 1 (one worker assumed to finish under global saturation).
        //   Pass 1: issue 950 placed at index 0 (repoA headroom → 0); issue 86 placed at index 1.
        //
        // Without the fix (ForClaim): repo A headroom = 0 (in-flight consumes the slot).
        //   Pass 1: issue 950 deferred (repoA saturated); issue 86 placed at index 0 → WRONG ORDER.
        int issue950Index = result.Items
            .Select((item, index) => (item, index))
            .First(t => t.item.IssueNumber == 950).index;
        int issue86Index = result.Items
            .Select((item, index) => (item, index))
            .First(t => t.item.IssueNumber == 86).index;

        issue950Index.ShouldSatisfyAllConditions(
            () => result.Items[issue950Index].IssueNumber.ShouldBe(950),
            () => result.Items[issue950Index].State.ShouldBe("revision_queued"),
            () => result.Items[issue950Index].RepositoryEligibilityStatus.ShouldBe("eligible"));

        issue86Index.ShouldSatisfyAllConditions(
            () => result.Items[issue86Index].IssueNumber.ShouldBe(86),
            () => result.Items[issue86Index].State.ShouldBe("queued"),
            () => result.Items[issue86Index].RepositoryEligibilityStatus.ShouldBe("eligible"));

        // The regression proof: repo A's revision_queued issue (950) must appear BEFORE repo B's fresh issue (86).
        issue950Index.ShouldBeLessThan(issue86Index,
            "revision_queued issue on the in-flight repo must precede the idle repo's fresh issue " +
            "because ForForecast restores repo A's headroom to 1 under global saturation");
    }
}
