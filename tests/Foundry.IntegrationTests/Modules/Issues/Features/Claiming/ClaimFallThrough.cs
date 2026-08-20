using Foundry.IntegrationTests.Modules.Monitoring;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.Claiming;

// Handler has no HTTP endpoint — dispatched through the integration-event pipeline from WorkerDispatchService.
// These tests resolve the handler from DI and call it directly against the real wired SQLite context,
// with real IRepositoryDispatchQueries and IRepositoryEligibilityQuery wiring (not stubs).
//
// Docker is NOT available in this sandbox — compile-verified only; executes in CI.
public sealed class ClaimFallThrough : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public ClaimFallThrough()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    /// <summary>
    /// Seeds a MonitoredRepository directly via DbContext that will be marked eligible but
    /// has no credential covering it, so IRepositoryDispatchQueries.GetDispatchInfoAsync returns null.
    /// Returns the MonitoredRepositoryId so the seeded issue can reference it.
    /// </summary>
    private async Task<MonitoredRepositoryId> SeedEligibleRepositoryWithoutDispatchInfoAsync(
        string slug,
        int position)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        // Seed via DbContext — no credential is associated with this repo,
        // so IRepositoryDispatchQueries.GetDispatchInfoAsync returns null (no credential covers the slug).
        RepositorySlug repositorySlug = RepositorySlug.Create(slug).ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(
            repositorySlug,
            host: "github.com",
            pollInterval: null,
            position: position);

        repo.SetEligibility(new RepositoryEligibility.Eligible());

        dbContext.Set<MonitoredRepository>().Add(repo);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repo.Id;
    }

    /// <summary>
    /// Seeds a MonitoredRepository that resolves dispatch info via the real pipeline.
    /// Uses AccountSeeder + RepositorySeeder + namespace + eligibility so the credential resolver matches.
    /// </summary>
    private async Task<MonitoredRepositoryId> SeedEligibleRepositoryWithDispatchInfoAsync(
        string token,
        string accountName,
        string slug,
        string owner)
    {
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: accountName,
            token: token);

        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: slug);

        // Set namespace so the credential resolver matches the slug owner.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, owner);

        // Mark repository as eligible so IRepositoryEligibilityQuery includes it in the candidate set.
        await RepositoryEligibilitySeeder.SetEligibleAsync(_factory, repositoryId);

        return MonitoredRepositoryId.From(repositoryId);
    }

    /// <summary>
    /// Seeds a QueuedIssue for the given repository directly via DbContext
    /// since no HTTP endpoint produces this state.
    /// </summary>
    private async Task<QueuedIssue> SeedQueuedIssueAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset detectedAt)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Fall-through integration test issue {issueNumber}",
            body: "Test body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: detectedAt);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);

        dbContext.Set<Issue>().Add(queued);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return queued;
    }

    [Fact]
    public async Task WhenBestCandidateRepoIsUnresolvable_ClaimsNextBestCandidate()
    {
        // Arrange — two eligible repositories. The unresolvable repo has a lower position
        // (higher dispatch priority) but GetDispatchInfoAsync returns null for it.
        // The resolvable repo has position 1 and real credentials.
        MonitoredRepositoryId unresolvableRepoId =
            await SeedEligibleRepositoryWithoutDispatchInfoAsync("no-cred-owner/no-cred-repo", position: 0);

        MonitoredRepositoryId resolvableRepoId = await SeedEligibleRepositoryWithDispatchInfoAsync(
            token: "ghp_fallthrough_token",
            accountName: "My GitHub FallThrough",
            slug: "resolvable-owner/resolvable-repo",
            owner: "resolvable-owner");

        // Both issues have the same detectedAt so position is the tiebreaker.
        DateTimeOffset sameTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        QueuedIssue unresolvableIssue = await SeedQueuedIssueAsync(unresolvableRepoId, issueNumber: 1, detectedAt: sameTime);
        QueuedIssue resolvableIssue = await SeedQueuedIssueAsync(resolvableRepoId, issueNumber: 2, detectedAt: sameTime);

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act — resolve the real handler from DI and invoke it directly.
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<WorkerCapacityAvailable> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<WorkerCapacityAvailable>>();
        await handler.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — next-best (resolvable) issue is claimed; unresolvable issue stays queued.
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();

        Issue? claimedIssue = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == resolvableIssue.Id, TestContext.Current.CancellationToken);
        Issue? skippedIssue = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == unresolvableIssue.Id, TestContext.Current.CancellationToken);

        claimedIssue.ShouldBeOfType<InProgressIssue>();
        skippedIssue.ShouldBeOfType<QueuedIssue>();
    }

    [Fact]
    public async Task WhenBestCandidateRepoIsUnresolvable_TickDoesNotAbort()
    {
        // Arrange — one unresolvable repo as best candidate, one resolvable as next-best.
        MonitoredRepositoryId unresolvableRepoId =
            await SeedEligibleRepositoryWithoutDispatchInfoAsync("fallthrough-nocred-owner/fallthrough-nocred-repo", position: 0);

        MonitoredRepositoryId resolvableRepoId = await SeedEligibleRepositoryWithDispatchInfoAsync(
            token: "ghp_fallthrough_noabort_token",
            accountName: "My GitHub NoAbort",
            slug: "fallthrough-owner/fallthrough-repo",
            owner: "fallthrough-owner");

        DateTimeOffset sameTime = DateTimeOffset.UtcNow.AddMinutes(-2);
        await SeedQueuedIssueAsync(unresolvableRepoId, issueNumber: 10, detectedAt: sameTime);
        QueuedIssue resolvableIssue = await SeedQueuedIssueAsync(resolvableRepoId, issueNumber: 11, detectedAt: sameTime);

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act — must not throw even when the best candidate is unresolvable.
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<WorkerCapacityAvailable> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<WorkerCapacityAvailable>>();
        await handler.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — the next-best issue from the resolvable repo was claimed (tick not aborted).
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == resolvableIssue.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<InProgressIssue>();
    }
}
