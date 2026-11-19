using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

using WorkerRunFailedEvent = Foundry.Modules.Workers.Contracts.WorkerRunFailed;

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.MonitoredRepositoryRemovedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset RemovedAt = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly FakeWorkerOrchestrator _orchestrator = new();
    private readonly CapturingIntegrationEventDispatcher _events = new();
    private readonly MonitoredRepositoryId _removedRepositoryId = MonitoredRepositoryId.New();

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
    public async Task ABlockedRun_FailsAsRepositoryRemoved()
    {
        // Arrange
        PublishBlockedRun blocked = Blocked(Publishing(_removedRepositoryId));
        await SeedAsync(blocked);

        // Act
        await DeliverAsync();

        // Assert
        (await LoadRunAsync(blocked.Id)).ShouldBeOfType<FailedRun>().Reason.ShouldBeOfType<FailureReason.RepositoryRemoved>();
    }

    [Fact]
    public async Task ABlockedRun_HasItsHandOffVolumeDeleted()
    {
        // Arrange
        PublishBlockedRun blocked = Blocked(Publishing(_removedRepositoryId));
        await SeedAsync(blocked);

        // Act
        await DeliverAsync();

        // Assert
        _orchestrator.DeletedHandOffVolumes.ShouldBe([blocked.Id]);
    }

    [Fact]
    public async Task ABlockedRun_RaisesWorkerRunFailedWithTheRepositoryRemovedCategory()
    {
        // Arrange
        PublishBlockedRun blocked = Blocked(Publishing(_removedRepositoryId));
        await SeedAsync(blocked);

        // Act
        await DeliverAsync();

        // Assert
        _events.DispatchedEvents.OfType<WorkerRunFailedEvent>().ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            failed => failed.WorkerRunId.ShouldBe(blocked.Id),
            failed => failed.Category.ShouldBe(FailureCategory.RepositoryRemovedToken));
    }

    [Fact]
    public async Task EveryPublishFailedRun_IsDiscardedWhetherExhaustedOrStillRetrying()
    {
        // Arrange
        PublishFailedRun exhausted = Publishing(_removedRepositoryId)
            .FailPublish("refused", transient: false, PublishingRunFixture.ExitedAt);
        PublishFailedRun retrying = Publishing(_removedRepositoryId)
            .FailPublish("timed out", transient: true, PublishingRunFixture.ExitedAt);
        await SeedAsync(exhausted);
        await SeedAsync(retrying);

        // Act
        await DeliverAsync();

        // Assert
        (await LoadRunAsync(exhausted.Id)).ShouldBeOfType<FailedRun>().Reason.ShouldBeOfType<FailureReason.RepositoryRemoved>();
        (await LoadRunAsync(retrying.Id)).ShouldBeOfType<FailedRun>().Reason.ShouldBeOfType<FailureReason.RepositoryRemoved>();
        _orchestrator.DeletedHandOffVolumes.ShouldBe([exhausted.Id, retrying.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task APublishingRun_IsLeftToPublish()
    {
        // Arrange
        PublishingRun publishing = Publishing(_removedRepositoryId);
        await SeedAsync(publishing);

        // Act
        await DeliverAsync();

        // Assert
        (await LoadRunAsync(publishing.Id)).ShouldBeOfType<PublishingRun>();
        _orchestrator.DeletedHandOffVolumes.ShouldBeEmpty();
        _events.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACompletedRun_IsKept()
    {
        // Arrange
        CompletedRun completed = Active(_removedRepositoryId).Complete(0, PublishingRunFixture.Branch, pullRequestUrl: null);
        await SeedAsync(completed);

        // Act
        await DeliverAsync();

        // Assert
        (await LoadRunAsync(completed.Id)).ShouldBeOfType<CompletedRun>();
        _orchestrator.DeletedHandOffVolumes.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnpublishedRunOfAnotherRepository_IsLeftAsItIs()
    {
        // Arrange
        PublishBlockedRun blocked = Blocked(Publishing(MonitoredRepositoryId.New()));
        await SeedAsync(blocked);

        // Act
        await DeliverAsync();

        // Assert
        (await LoadRunAsync(blocked.Id)).ShouldBeOfType<PublishBlockedRun>();
        _orchestrator.DeletedHandOffVolumes.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRedelivered_DiscardsNothingAgain()
    {
        // Arrange
        PublishBlockedRun blocked = Blocked(Publishing(_removedRepositoryId));
        await SeedAsync(blocked);
        await DeliverAsync();

        // Act
        await DeliverAsync();

        // Assert
        _orchestrator.DeletedHandOffVolumes.ShouldBe([blocked.Id]);
        _events.DispatchedEvents.OfType<WorkerRunFailedEvent>().ShouldHaveSingleItem();
    }

    private static ActiveRun Active(MonitoredRepositoryId repositoryId)
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordPreparedBase(PublishingRunFixture.Base, repositoryId);
        return starting.Activate(ContainerId.From("container-1"), PublishingRunFixture.Branch, repositoryId);
    }

    private static PublishingRun Publishing(MonitoredRepositoryId repositoryId) =>
        Active(repositoryId).BeginPublishing(
            new RunExit(0, OomKilled: false, RunStopReason.Exited, PublishingRunFixture.ExitedAt),
            containerOutput: "log tail",
            RunVerdict.Done(),
            new RunPullRequestText("Add retry", new RunPullRequestDescription(null, ["Body"], ["dotnet test: passed."], [], null)));

    private static PublishBlockedRun Blocked(PublishingRun publishing) =>
        publishing.BlockPublish(
            [new SecretFinding("a.env", 1, CommitSha.From("3333333333333333333333333333333333333333"), "github-pat")],
            PublishingRunFixture.ExitedAt);

    private async Task SeedAsync(WorkerRun run)
    {
        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<WorkerRun> LoadRunAsync(WorkerRunId id)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<WorkerRun>().SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
    }

    private async Task DeliverAsync()
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IIntegrationEventDispatcher>(_events)
            .AddTransient<IDomainEventHandler<Foundry.Modules.Workers.Domain.Events.WorkerRunFailed>, WorkerRunFailedBridgeHandler>()
            .BuildServiceProvider();
        MonitoredRepositoryRemovedHandler sut = new(
            _dbContext,
            _orchestrator,
            new DomainEventDispatcher(services),
            NullLogger<MonitoredRepositoryRemovedHandler>.Instance);
        await sut.HandleAsync(
            new MonitoredRepositoryRemoved(_removedRepositoryId, RemovedAt),
            TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }
}
