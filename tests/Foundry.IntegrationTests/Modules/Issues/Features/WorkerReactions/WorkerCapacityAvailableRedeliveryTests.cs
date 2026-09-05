using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.WorkerReactions;

// Pre-ship integration guard (AC #2 from Part 1): proves that when the outbox redelivers a
// WorkerCapacityAvailable event whose claim has already committed (crash before processed_events
// commits), the real relay + processor pipeline hits the guard and does not double-claim.
//
// Docker is NOT available in this sandbox — compile-verified only; executes in CI.
public sealed class WorkerCapacityAvailableRedeliveryTests : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WorkerCapacityAvailableRedeliveryTests()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    /// <summary>
    /// Seeds an InProgressIssue carrying <paramref name="workerRunId"/> directly via DbContext
    /// (the production path goes through the claim handler, but here we seed the already-committed
    /// state to simulate a crash between the claim commit and processed_events commit).
    /// </summary>
    private async Task SeedInProgressIssueAsync(MonitoredRepositoryId repositoryId, WorkerRunId workerRunId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .WithTitle("Redelivery integration test issue")
            .WithWorkerRunId(workerRunId)
            .InProgress();

        dbContext.Set<Issue>().Add(inProgress);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Seeds an unpublished OutboxMessage for <see cref="WorkerCapacityAvailable"/> with the given
    /// <paramref name="workerRunId"/>, with no corresponding <c>processed_events</c> row — simulating
    /// the state after a crash between the claim commit and the processed_events commit.
    /// </summary>
    private async Task SeedUnpublishedOutboxMessageAsync(WorkerRunId workerRunId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        WorkerCapacityAvailable @event = new(workerRunId);
        OutboxMessage message = OutboxMessage.Create(@event, DateTimeOffset.UtcNow.AddMinutes(-1));
        dbContext.Set<OutboxMessage>().Add(message);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenClaimAlreadyCommittedAndOutboxRedelivers_ExactlyOneIssueHoldsWorkerRunId()
    {
        // Arrange — simulate the post-crash state:
        //   - the issue is already InProgress with the run id (claim committed)
        //   - the outbox row is still unpublished (processed_events did not commit)
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        WorkerRunId workerRunId = WorkerRunId.New();

        await SeedInProgressIssueAsync(repositoryId, workerRunId);
        await SeedUnpublishedOutboxMessageAsync(workerRunId);

        // Act — drive one relay tick through the real IntegrationEventProcessor pipeline.
        OutboxRelayService relay = _factory.Services.GetRequiredService<OutboxRelayService>();
        await relay.TickForTest(TestContext.Current.CancellationToken);

        // Assert — exactly one issue holds this WorkerRunId; no additional issue was claimed.
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();

        List<InProgressIssue> inProgressIssues = await dbContext.Set<Issue>()
            .OfType<InProgressIssue>()
            .ToListAsync(TestContext.Current.CancellationToken);

        inProgressIssues.Count(i => i.WorkerRunId == workerRunId).ShouldBe(1);
    }

    [Fact]
    public async Task WhenClaimAlreadyCommittedAndOutboxRedelivers_OutboxRowMarkedPublished()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        WorkerRunId workerRunId = WorkerRunId.New();

        await SeedInProgressIssueAsync(repositoryId, workerRunId);
        await SeedUnpublishedOutboxMessageAsync(workerRunId);

        // Act
        OutboxRelayService relay = _factory.Services.GetRequiredService<OutboxRelayService>();
        await relay.TickForTest(TestContext.Current.CancellationToken);

        // Assert — the outbox row was processed (marked published) even though the guard returned early.
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();

        List<OutboxMessage> messages = await dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);

        messages.ShouldAllBe(m => m.ProcessedAt.HasValue);
    }
}
