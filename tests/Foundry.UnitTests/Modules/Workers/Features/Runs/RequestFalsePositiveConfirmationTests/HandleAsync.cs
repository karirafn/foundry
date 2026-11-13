using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.RequestFalsePositiveConfirmationTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private static readonly SecretFinding Finding = new(
        "src/store.ts",
        3,
        CommitSha.From("3333333333333333333333333333333333333333"),
        "generic-api-key");

    private static readonly DateTimeOffset BlockedAt = PublishingRunFixture.ExitedAt.AddMinutes(1);

    private static readonly DateTimeOffset Now = PublishingRunFixture.ExitedAt.AddHours(1);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventDispatcher _integrationEvents = new();

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ABlockedRunConfirmedAtItsBlockTime_PublishesWithTheFindingsConfirmed()
    {
        // Arrange
        PublishBlockedRun blocked = PublishingRunFixture.Publishing().BlockPublish([Finding], BlockedAt);
        await SeedAsync(blocked);

        // Act
        Result<RequestFalsePositiveConfirmation.Response> result = await ConfirmAsync(blocked.Id, BlockedAt);

        // Assert
        result.ShouldBeOfType<Result<RequestFalsePositiveConfirmation.Response>.Success>();
        PublishingRun publishing = (await ReloadAsync()).ShouldBeOfType<PublishingRun>();
        publishing.ShouldSatisfyAllConditions(
            () => publishing.ConfirmedFindings.ShouldBe([Finding]),
            () => publishing.FalsePositivesConfirmedAt.ShouldBe(Now));
    }

    [Fact]
    public async Task AConfirmation_RaisesTheConfirmedEventAndThePublishingPhase()
    {
        // Arrange
        PublishBlockedRun blocked = PublishingRunFixture.Publishing().BlockPublish([Finding], BlockedAt);
        await SeedAsync(blocked);

        // Act
        await ConfirmAsync(blocked.Id, BlockedAt);

        // Assert
        _integrationEvents.DispatchedEvents.ShouldBe(
        [
            new WorkerRunFalsePositivesConfirmed(blocked.Id, blocked.IssueId.Value),
            new WorkerRunPublishPhaseChanged(blocked.Id, blocked.IssueId.Value, WorkerRunPublishPhase.Publishing),
        ]);
    }

    [Fact]
    public async Task AConfirmationOfAnEarlierBlock_IsRejectedAndLeavesTheRunBlocked()
    {
        // Arrange
        PublishBlockedRun blocked = PublishingRunFixture.Publishing().BlockPublish([Finding], BlockedAt);
        await SeedAsync(blocked);

        // Act
        Result<RequestFalsePositiveConfirmation.Response> result = await ConfirmAsync(blocked.Id, BlockedAt.AddSeconds(-1));

        // Assert
        result.ShouldBeOfType<Result<RequestFalsePositiveConfirmation.Response>.Failure>()
            .Error.Code.ShouldBe("WorkerRun.FindingsChanged");
        (await ReloadAsync()).ShouldBeOfType<PublishBlockedRun>();
        _integrationEvents.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAlreadyConfirmedRun_IsNotBlocked()
    {
        // Arrange
        PublishingRun publishing = PublishingRunFixture.Publishing().BlockPublish([Finding], BlockedAt).ConfirmFalsePositives(Now);
        await SeedAsync(publishing);

        // Act
        Result<RequestFalsePositiveConfirmation.Response> result = await ConfirmAsync(publishing.Id, BlockedAt);

        // Assert
        result.ShouldBeOfType<Result<RequestFalsePositiveConfirmation.Response>.Failure>()
            .Error.Code.ShouldBe("WorkerRun.NotPublishBlocked");
    }

    [Fact]
    public async Task ADiscardedRun_IsNotBlocked()
    {
        // Arrange
        FailedRun discarded = PublishingRunFixture.Publishing().BlockPublish([Finding], BlockedAt).DiscardOnRequest();
        await SeedAsync(discarded);

        // Act
        Result<RequestFalsePositiveConfirmation.Response> result = await ConfirmAsync(discarded.Id, BlockedAt);

        // Assert
        result.ShouldBeOfType<Result<RequestFalsePositiveConfirmation.Response>.Failure>()
            .Error.Code.ShouldBe("WorkerRun.NotPublishBlocked");
    }

    [Fact]
    public async Task AnUnknownRun_IsNotFound()
    {
        // Act
        Result<RequestFalsePositiveConfirmation.Response> result = await ConfirmAsync(WorkerRunId.New(), BlockedAt);

        // Assert
        result.ShouldBeOfType<Result<RequestFalsePositiveConfirmation.Response>.Failure>()
            .Error.Code.ShouldBe("WorkerRun.NotFound");
    }

    private async Task SeedAsync(WorkerRun run)
    {
        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<WorkerRun> ReloadAsync()
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
    }

    private Task<Result<RequestFalsePositiveConfirmation.Response>> ConfirmAsync(WorkerRunId id, DateTimeOffset publishBlockedAt)
    {
        RequestFalsePositiveConfirmation.Handler sut = new(
            _dbContext,
            _integrationEvents,
            new CapturingDomainEventDispatcher(),
            new FixedTimeProvider(Now));
        return sut.HandleAsync(
            new RequestFalsePositiveConfirmation.Command(id, publishBlockedAt),
            TestContext.Current.CancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
