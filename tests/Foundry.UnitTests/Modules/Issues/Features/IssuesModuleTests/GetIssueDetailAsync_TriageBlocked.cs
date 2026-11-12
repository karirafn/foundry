using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetIssueDetailAsync_TriageBlocked : IAsyncDisposable
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    public GetIssueDetailAsync_TriageBlocked()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        StubRepositorySlugQueries slugQueries = new();
        slugQueries.AddSlug(RepositoryId, "owner/repo", "github");
        _sut = new IssueQueries(
            _dbContext,
            slugQueries,
            new NullRepositoryEligibilityQuery(),
            new NullWorkerRunQueries(),
            new InFlightWorkerCountQuery(_dbContext),
            new NullGlobalSettingsQueries());
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenTriageBlocked_ServesTheStateNameAndItsBlockers()
    {
        // Arrange
        Issue blocked = new IssueBuilder().WithMonitoredRepositoryId(RepositoryId).TriageBlocked([3, 5]);
        _dbContext.Set<Issue>().Add(blocked);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(blocked.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueDetail detail = result.ShouldBeOfType<Result<IssueDetail>.Success>().Value;
        detail.ShouldSatisfyAllConditions(
            () => detail.State.ShouldBe("triage_blocked"),
            () => detail.StateDetails.ShouldNotBeNull().BlockedBy.ShouldNotBeNull().Select(b => b.IssueNumber).ShouldBe([3, 5]));
    }

    [Fact]
    public async Task WhenTriageBlockedByAVerdict_ServesTheBlockedOutcomeWithNoQuestions()
    {
        // Arrange
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(3, "Needs the retry queue.")],
            DateTimeOffset.UtcNow);
        TriageBlockedIssue blocked = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .TriageBlockedFromTriage(verdict);
        _dbContext.Set<Issue>().Add(blocked);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(blocked.Id, TestContext.Current.CancellationToken);

        // Assert
        TriageVerdictDetails triage = result.ShouldBeOfType<Result<IssueDetail>.Success>().Value.Triage.ShouldNotBeNull();
        triage.ShouldSatisfyAllConditions(
            () => triage.Outcome.ShouldBe(TriageVerdictDetails.BlockedOutcome),
            () => triage.Kind.ShouldBe(verdict.Kind.Value),
            () => triage.Questions.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenBlockedAfterAReadyVerdict_ServesNoReasons()
    {
        // Arrange
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .FreshQueuedFromTriage(IssueKind.Feature);
        BlockedIssue blocked = queued.Block([3, 5]);
        _dbContext.Set<Issue>().Add(blocked);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(blocked.Id, TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<BlockerDetails> blockers = result
            .ShouldBeOfType<Result<IssueDetail>.Success>()
            .Value.StateDetails.ShouldNotBeNull()
            .BlockedBy.ShouldNotBeNull();
        blockers.Select(b => (b.IssueNumber, b.Reason)).ShouldBe([(3, (string?)null), (5, (string?)null)]);
    }

    [Fact]
    public async Task WhenTriageBlockedByAVerdict_ServesEachNamedBlockersReasonAndNoReasonForALinkedOne()
    {
        // Arrange
        TriageInProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(RepositoryId).TriageInProgress();
        inProgress.SetBlockedBy([7]);
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(3, "Needs the retry queue."), new TriageBlocker(5, "Needs the schema.")],
            DateTimeOffset.UtcNow);
        TriageBlockedIssue blocked = inProgress.MarkBlocked(verdict);
        _dbContext.Set<Issue>().Add(blocked);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(blocked.Id, TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<BlockerDetails> blockers = result
            .ShouldBeOfType<Result<IssueDetail>.Success>()
            .Value.StateDetails.ShouldNotBeNull()
            .BlockedBy.ShouldNotBeNull();
        blockers.Select(b => (b.IssueNumber, b.Reason)).ShouldBe(
            [(3, "Needs the retry queue."), (5, "Needs the schema."), (7, (string?)null)]);
    }
}
