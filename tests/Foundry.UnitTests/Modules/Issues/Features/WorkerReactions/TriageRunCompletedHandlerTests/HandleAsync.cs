using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.WorkerReactions;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.TriageRunCompletedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<TriageRunCompleted> _sut;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _dispatcher = new CapturingDomainEventDispatcher();
        _sut = new TriageRunCompletedHandler(_dbContext, _dispatcher, NullLogger<TriageRunCompletedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private TriageInProgressIssue Seed(TriageInProgressIssue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    private async Task<Issue> ReloadAsync(Issue issue)
    {
        _dbContext.ChangeTracker.Clear();
        Issue? reloaded = await _dbContext.Set<Issue>().FindAsync([issue.Id], TestContext.Current.CancellationToken);
        return reloaded.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenReady_TheUnclassifiedIssueIsQueuedTriagedWithTheVerdictRecorded()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        TriageInProgressIssue triaging = Seed(new IssueBuilder()
            .WithWorkerRunId(workerRunId)
            .WithKindClassification(KindClassification.Unclassified)
            .TriageInProgress());

        // Act
        await _sut.HandleAsync(
            new TriageRunCompleted(triaging.Id.Value, workerRunId, new TriageResult.Ready(TriageKind.Bug), DecidedAt),
            CancellationToken.None);

        // Assert
        FreshQueuedIssue queued = (await ReloadAsync(triaging)).ShouldBeOfType<FreshQueuedIssue>();
        queued.ShouldSatisfyAllConditions(
            () => queued.KindClassification.ShouldBe(new KindClassification.Triaged(IssueKind.Bug)),
            () => queued.TriageVerdict.ShouldBe(new TriageVerdict.Ready(IssueKind.Bug, DecidedAt)),
            () => _dispatcher.DispatchedEvents.OfType<IssueQueued>().ShouldHaveSingleItem());
    }

    [Fact]
    public async Task WhenReadyWithOpenBlockers_TheIssueIsBlocked()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        TriageQueuedIssue queued = new IssueBuilder().TriageQueued();
        queued.SetBlockedBy([5]);
        TriageInProgressIssue triaging = Seed(queued.Claim(workerRunId));

        // Act
        await _sut.HandleAsync(
            new TriageRunCompleted(triaging.Id.Value, workerRunId, new TriageResult.Ready(TriageKind.Feature), DecidedAt),
            CancellationToken.None);

        // Assert
        (await ReloadAsync(triaging)).ShouldBeOfType<BlockedIssue>().BlockedBy.ShouldBe([5]);
    }

    [Fact]
    public async Task WhenNeedsInput_TheIssueAwaitsInputToRetriage()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        TriageInProgressIssue triaging = Seed(new IssueBuilder().WithWorkerRunId(workerRunId).TriageInProgress());
        IReadOnlyList<InputQuestion> questions = [new InputQuestion("Which endpoint?", "Two match.", "The v2 one.")];

        // Act
        await _sut.HandleAsync(
            new TriageRunCompleted(
                triaging.Id.Value,
                workerRunId,
                new TriageResult.NeedsInput(TriageKind.Feature, questions),
                DecidedAt),
            CancellationToken.None);

        // Assert
        AwaitingInputIssue awaiting = (await ReloadAsync(triaging)).ShouldBeOfType<AwaitingInputIssue>();
        awaiting.ShouldSatisfyAllConditions(
            () => awaiting.ResumeTarget.ShouldBeOfType<ResumeTarget.Retriage>(),
            () => awaiting.Questions.ShouldBe(questions),
            () => awaiting.TriageVerdict.ShouldBeOfType<TriageVerdict.NeedsInput>().Questions.ShouldBe(questions),
            () => _dispatcher.DispatchedEvents.OfType<IssueAwaitingInput>().ShouldHaveSingleItem());
    }

    [Fact]
    public async Task WhenTheIssueIsNoLongerInTriage_NothingHappens()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        FreshQueuedIssue queued = new IssueBuilder().FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        await _sut.HandleAsync(
            new TriageRunCompleted(queued.Id.Value, workerRunId, new TriageResult.Ready(TriageKind.Bug), DecidedAt),
            CancellationToken.None);

        // Assert
        Issue reloaded = await ReloadAsync(queued);
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.ShouldBeOfType<FreshQueuedIssue>(),
            () => reloaded.TriageVerdict.ShouldBeNull(),
            () => _dispatcher.DispatchedEvents.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenTheRunIdIsStale_NothingHappens()
    {
        // Arrange
        TriageInProgressIssue triaging = Seed(new IssueBuilder().WithWorkerRunId(WorkerRunId.New()).TriageInProgress());

        // Act
        await _sut.HandleAsync(
            new TriageRunCompleted(triaging.Id.Value, WorkerRunId.New(), new TriageResult.Ready(TriageKind.Bug), DecidedAt),
            CancellationToken.None);

        // Assert
        (await ReloadAsync(triaging)).ShouldBeOfType<TriageInProgressIssue>();
    }
}
