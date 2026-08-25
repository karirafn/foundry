using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Persistence.MonitoredRepositoryEligibilityTests;

/// <summary>
/// Proves the bug from #465: a monitored_repositories row whose write_probe_verdict column is NULL
/// (the state every row starts in after the AddWriteProbeVerdict migration) self-heals to Eligible
/// in a single poll cycle. The NULL column cannot be produced via any HTTP endpoint — the POST
/// /repositories endpoint runs a full evaluation immediately — so the row is written directly via
/// raw SQL after EF has already serialized a JSON verdict.
/// </summary>
public sealed class WhenWriteProbeVerdictIsNull : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenWriteProbeVerdictIsNull()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            // Replace the real eligibility evaluator with a stub that immediately grants write
            // access and marks the repository eligible, so the test exercises the routing
            // decision (IsDueForWriteProbe → full path) without requiring real GitHub creds.
            services.RemoveAll<IRepositoryEligibilityEvaluator>();
            services.AddScoped<IRepositoryEligibilityEvaluator, GrantingEligibilityEvaluator>();
        });
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task AfterOnePollCycle_RepositoryBecomesEligible()
    {
        // Arrange — seed a repository with a covering credential, then write NULL
        // to the write_probe_verdict column to simulate a migration-backfilled row.
        // No poll interval is set so the IsDueForPoll check does not filter it out
        // (null poll interval uses the service default of 30 s, and we pass a far-future now).
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

        MonitoredRepositoryId repositoryId = await SeedRepositoryWithNullVerdictAsync();

        DateTimeOffset now = DateTimeOffset.UtcNow.AddHours(1);

        // Act — resolve RepositoryPoller from the factory's DI (which uses the granting evaluator),
        // then drive one poll cycle with a stub provider that returns an empty complete listing.
        // RepositoryPoller is scoped; create a scope that lives for the duration of the poll.
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

            MonitoredRepository? repository = await dbContext
                .Set<MonitoredRepository>()
                .FirstOrDefaultAsync(
                    r => r.Id == repositoryId,
                    TestContext.Current.CancellationToken);

            repository.ShouldNotBeNull();

            RepositoryPoller poller = scope.ServiceProvider.GetRequiredService<RepositoryPoller>();

            await poller.PollAsync(
                repository,
                new EmptyCompleteIssueProvider(),
                now,
                TestContext.Current.CancellationToken);
        }

        // Assert — reload from a fresh scope to exercise the full EF round-trip.
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

            MonitoredRepository? reloaded = await dbContext
                .Set<MonitoredRepository>()
                .FirstOrDefaultAsync(
                    r => r.Id == repositoryId,
                    TestContext.Current.CancellationToken);

            reloaded.ShouldNotBeNull();
            reloaded.Eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>();
            reloaded.EligibilityStatus.ShouldBe("eligible");
        }
    }

    private async Task<MonitoredRepositoryId> SeedRepositoryWithNullVerdictAsync()
    {
        // Seed the repository through DbContext so we control the write_probe_verdict column.
        // EF serializes a default Unknown() verdict as JSON, so we overwrite it with NULL
        // afterwards via raw SQL to reproduce the exact post-migration state (#465).
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        RepositorySlug slug = RepositorySlug.Create("owner/null-verdict-repo").ValueOrThrow();
        MonitoredRepository repository = MonitoredRepository.Create(slug, "github.com", pollInterval: null);

        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Overwrite the serialized Unknown JSON with NULL to simulate the migration state.
        // The HTTP path cannot produce this: POST /repositories runs a full evaluation first.
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE monitored_repositories SET write_probe_verdict = NULL WHERE id = {0}",
            repository.Id.Value.ToString());

        return repository.Id;
    }

    // Grants write access and marks the repository eligible unconditionally.
    private sealed class GrantingEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            repo.SetEligibility(new RepositoryEligibility.Eligible());
            repo.SetWriteProbeVerdict(new WriteProbeVerdict.Granted());
            return Task.CompletedTask;
        }

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
        {
            repo.SetEligibility(new RepositoryEligibility.Eligible());
            return Task.CompletedTask;
        }
    }

    // Returns an empty, complete listing so the poll cycle advances past the issue-detection
    // passes without making any real HTTP calls.
    private sealed class EmptyCompleteIssueProvider : IIssueProvider
    {
        public Task<Result<IssueListing>> GetIssuesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IssueListing>.Ok(new IssueListing([], IsComplete: true)));

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<int>>.Ok([]));

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(false));

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)));

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([])));

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<BranchProtection>.Ok(
                new BranchProtection("main", false, false, false)));

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<MergeRequestByBranch>.Ok(
                new MergeRequestByBranch(MergeRequestPresence.None, WebUrl: null)));

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<BranchCommitSummary>.Ok(new BranchCommitSummary(CommitCount: 0, LatestSha: null)));

        public Task<Result<bool>> CanPushAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(true));
    }
}
