using Foundry.Modules.Issues.Contracts.Events;
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

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.IssueClosedWithUnpublishedRunHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly FakeWorkerOrchestrator _orchestrator = new();
    private readonly CapturingIntegrationEventDispatcher _events = new();

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
    public async Task ABlockedRun_FailsAsIssueClosed()
    {
        // Arrange
        PublishBlockedRun blocked = Blocked(Publishing());
        await SeedAsync(blocked);

        // Act
        await DeliverAsync(blocked.Id);

        // Assert
        (await LoadRunAsync()).ShouldBeOfType<FailedRun>().Reason.ShouldBeOfType<FailureReason.IssueClosed>();
    }

    [Fact]
    public async Task ABlockedRun_HasItsHandOffVolumeDeleted()
    {
        // Arrange
        PublishBlockedRun blocked = Blocked(Publishing());
        await SeedAsync(blocked);

        // Act
        await DeliverAsync(blocked.Id);

        // Assert
        _orchestrator.DeletedHandOffVolumes.ShouldBe([blocked.Id]);
    }

    [Fact]
    public async Task ABlockedRun_KeepsItsTranscript()
    {
        // Arrange
        PublishBlockedRun blocked = Blocked(Publishing(
            WorkerRunTranscript.Capture(WorkerRunId.New(), "rendered transcript", PublishingRunFixture.ExitedAt)));
        await SeedAsync(blocked);

        // Act
        await DeliverAsync(blocked.Id);

        // Assert
        (await _dbContext.Set<WorkerRunTranscript>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldSatisfyAllConditions(
                transcript => transcript.Id.ShouldBe(blocked.Id),
                transcript => transcript.Transcript.ShouldBe("rendered transcript"));
    }

    [Fact]
    public async Task ABlockedRun_RaisesWorkerRunFailedWithTheIssueClosedCategory()
    {
        // Arrange
        PublishBlockedRun blocked = Blocked(Publishing());
        await SeedAsync(blocked);

        // Act
        await DeliverAsync(blocked.Id);

        // Assert
        _events.DispatchedEvents.OfType<WorkerRunFailedEvent>().ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            failed => failed.WorkerRunId.ShouldBe(blocked.Id),
            failed => failed.Category.ShouldBe(FailureCategory.IssueClosedToken),
            failed => failed.BranchName.ShouldBe(PublishingRunFixture.Branch.Value));
    }

    [Fact]
    public async Task AnExhaustedPublishFailedRun_FailsAsIssueClosed()
    {
        // Arrange
        PublishFailedRun failed = Publishing().FailPublish("refused", transient: false, PublishingRunFixture.ExitedAt);
        await SeedAsync(failed);

        // Act
        await DeliverAsync(failed.Id);

        // Assert
        (await LoadRunAsync()).ShouldBeOfType<FailedRun>().Reason.ShouldBeOfType<FailureReason.IssueClosed>();
        _orchestrator.DeletedHandOffVolumes.ShouldBe([failed.Id]);
    }

    [Fact]
    public async Task APublishFailedRunStillRetrying_FailsAsIssueClosed()
    {
        // Arrange
        PublishFailedRun retrying = Publishing().FailPublish("timed out", transient: true, PublishingRunFixture.ExitedAt);
        await SeedAsync(retrying);

        // Act
        await DeliverAsync(retrying.Id);

        // Assert
        (await LoadRunAsync()).ShouldBeOfType<FailedRun>().Reason.ShouldBeOfType<FailureReason.IssueClosed>();
        _orchestrator.DeletedHandOffVolumes.ShouldBe([retrying.Id]);
    }

    [Fact]
    public async Task AnAlreadyDiscardedRun_IsLeftAsItIs()
    {
        // Arrange
        FailedRun discarded = Blocked(Publishing()).DiscardOnRequest();
        await SeedAsync(discarded);

        // Act
        await DeliverAsync(discarded.Id);

        // Assert
        (await LoadRunAsync()).ShouldBeOfType<FailedRun>().Reason.ShouldBeOfType<FailureReason.RunDiscarded>();
        _orchestrator.DeletedHandOffVolumes.ShouldBeEmpty();
        _events.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task APublishingRun_IsLeftToPublish()
    {
        // Arrange
        PublishingRun publishing = Publishing();
        await SeedAsync(publishing);

        // Act
        await DeliverAsync(publishing.Id);

        // Assert
        (await LoadRunAsync()).ShouldBeOfType<PublishingRun>();
        _orchestrator.DeletedHandOffVolumes.ShouldBeEmpty();
        _events.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACompletedRun_IsLeftCompleted()
    {
        // Arrange
        CompletedRun completed = PublishingRunFixture.ActiveRun()
            .Complete(0, PublishingRunFixture.Branch, pullRequestUrl: null);
        await SeedAsync(completed);

        // Act
        await DeliverAsync(completed.Id);

        // Assert
        (await LoadRunAsync()).ShouldBeOfType<CompletedRun>();
        _orchestrator.DeletedHandOffVolumes.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownRun_IsANoOp()
    {
        // Act
        await DeliverAsync(WorkerRunId.New());

        // Assert
        (await _dbContext.Set<WorkerRun>().AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        _orchestrator.DeletedHandOffVolumes.ShouldBeEmpty();
        _events.DispatchedEvents.ShouldBeEmpty();
    }

    private static PublishingRun Publishing(WorkerRunTranscript? transcript = null) =>
        PublishingRunFixture.ActiveRun().BeginPublishing(
            new RunExit(0, OomKilled: false, RunStopReason.Exited, PublishingRunFixture.ExitedAt),
            containerOutput: "log tail",
            RunVerdict.Done(),
            new RunPullRequestText("Add retry", "Body"),
            transcript);

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

    private async Task<WorkerRun> LoadRunAsync()
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
    }

    private Task DeliverAsync(WorkerRunId id)
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IIntegrationEventDispatcher>(_events)
            .AddTransient<IDomainEventHandler<Foundry.Modules.Workers.Domain.Events.WorkerRunFailed>, WorkerRunFailedBridgeHandler>()
            .BuildServiceProvider();
        IssueClosedWithUnpublishedRunHandler sut = new(
            _dbContext,
            _orchestrator,
            new DomainEventDispatcher(services),
            NullLogger<IssueClosedWithUnpublishedRunHandler>.Instance);
        return sut.HandleAsync(new IssueClosedWithUnpublishedRun(id), TestContext.Current.CancellationToken);
    }
}
