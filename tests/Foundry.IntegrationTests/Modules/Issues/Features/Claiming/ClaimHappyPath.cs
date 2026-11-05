using Foundry.IntegrationTests.Modules.Monitoring;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.Claiming;

// Handler has no HTTP endpoint — dispatched through the integration-event pipeline from WorkerDispatchService.
// These tests resolve the handler from DI and call it directly against the real wired SQLite context,
// with real IRepositoryDispatchQueries and IRepositoryEligibilityQuery wiring (not stubs).
//
// Docker is NOT available in this sandbox — compile-verified only; executes in CI.
public sealed class ClaimHappyPath : IAsyncDisposable
{
    private readonly StubIssueBlockerQueries _blockerQueries = new();
    private readonly FoundryWebAppFactory _factory;

    public ClaimHappyPath()
    {
        // The claim-time blocker re-check (#731) reads blockers live from the provider; the seeded
        // token is fake, so substitute a stub that reports blockers per issue.
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IIssueBlockerQueries>();
            services.AddSingleton<IIssueBlockerQueries>(_blockerQueries);
        });
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    /// <summary>
    /// Seeds a MonitoredRepository that resolves dispatch info via the real pipeline:
    /// - POST /api/accounts/{id}/repositories creates the repo with slug "owner/repo"
    /// - SetOwnerNamespacesAsync makes the credential cover the "owner" namespace
    /// - SetEligibleAsync stamps the eligibility row so the selector includes the repo
    /// </summary>
    private async Task<MonitoredRepositoryId> SeedEligibleRepositoryWithDispatchInfoAsync()
    {
        // Seed a GitHub credential so IRepositoryDispatchQueries resolves real dispatch info.
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, token: "ghp_test_happy_path");

        // Create the repository via the POST endpoint (slug "owner/repo", covered by the credential).
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/repo");

        // Set namespace so the credential resolver matches "owner/repo".
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

        // Mark repository as eligible so IRepositoryEligibilityQuery includes it in the candidate set.
        await RepositoryEligibilitySeeder.SetEligibleAsync(_factory, repositoryId);

        return MonitoredRepositoryId.From(repositoryId);
    }

    /// <summary>
    /// Seeds a FreshQueuedIssue pointing at the given repository directly via DbContext,
    /// since no HTTP endpoint exists to produce this state.
    /// </summary>
    private async Task<FreshQueuedIssue> SeedQueuedIssueAsync(MonitoredRepositoryId repositoryId, int issueNumber = 1)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle("Claim integration test issue")
            .WithLabels(["foundry"])
            .FreshQueued();

        dbContext.Set<Issue>().Add(queued);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return queued;
    }

    [Fact]
    public async Task WhenEligibleRepositoryAndQueuedIssue_TransitionsIssueToInProgress()
    {
        // Arrange — seed a repository with real credentials and eligibility, plus a queued issue.
        MonitoredRepositoryId repositoryId = await SeedEligibleRepositoryWithDispatchInfoAsync();
        FreshQueuedIssue queued = await SeedQueuedIssueAsync(repositoryId);
        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act — resolve the real handler from DI and invoke it directly (no HTTP endpoint exists).
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<WorkerCapacityAvailable> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<WorkerCapacityAvailable>>();
        await handler.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — issue transitioned from FreshQueuedIssue to InProgressIssue via the real wiring.
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == queued.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public async Task WhenEligibleRepositoryAndQueuedIssue_IssuedClaimedOutboxRowHarvested()
    {
        // Arrange — seed a repository with real credentials and eligibility, plus a queued issue.
        MonitoredRepositoryId repositoryId = await SeedEligibleRepositoryWithDispatchInfoAsync();
        await SeedQueuedIssueAsync(repositoryId, issueNumber: 2);
        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act — resolve the real handler from DI and invoke it directly.
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<WorkerCapacityAvailable> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<WorkerCapacityAvailable>>();
        await handler.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — IssueClaimed outbox row was written atomically with the claim transition.
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        List<OutboxMessage> messages = await dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(IssueClaimed)));
    }

    [Fact]
    public async Task WhenQueuedIssueGainedBlockerOnProvider_BlocksIssueAndSkipsClaim()
    {
        // Arrange — the provider reports an open blocker Foundry has not yet recorded.
        MonitoredRepositoryId repositoryId = await SeedEligibleRepositoryWithDispatchInfoAsync();
        FreshQueuedIssue queued = await SeedQueuedIssueAsync(repositoryId, issueNumber: 3);
        _blockerQueries.WithBlockers(3, 1);
        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<WorkerCapacityAvailable> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<WorkerCapacityAvailable>>();
        await handler.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — the issue is blocked with the provider's blocker and nothing is claimed.
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == queued.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<BlockedIssue>().BlockedBy.ShouldBe([1]);

        List<OutboxMessage> messages = await dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldNotContain(m => m.Type.Contains(nameof(IssueClaimed)));
    }
}
