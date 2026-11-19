using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

/// <summary>
/// The issue detail's catch-up fields (ADR 0111): the rebase-pending reason, the catch-up round and the merge wait,
/// read from the issue's catch-up queue entry.
/// </summary>
public sealed class GetIssueDetailAsync_CatchUp : IAsyncDisposable
{
    private const int IssueNumber = 42;
    private const int RoundLimit = 3;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly StubMergeQueueQueries _mergeQueue = new();
    private readonly IIssueQueries _sut;

    public GetIssueDetailAsync_CatchUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        StubRepositorySlugQueries slugQueries = new();
        slugQueries.AddSlug(RepositoryId, "owner/repo");
        _sut = new IssueQueries(
            _dbContext,
            slugQueries,
            new NullRepositoryEligibilityQuery(),
            new NullWorkerRunQueries(),
            new InFlightWorkerCountQuery(_dbContext, new NullWorkerRunQueries()),
            new NullGlobalSettingsQueries(),
            _mergeQueue);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static IssueBuilder Builder() =>
        new IssueBuilder().WithMonitoredRepositoryId(RepositoryId).WithIssueNumber(IssueNumber);

    private async Task<IssueStateDetails> DetailsOfAsync(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(issue.Id, TestContext.Current.CancellationToken);
        return result.ShouldBeOfType<Result<IssueDetail>.Success>().Value.StateDetails.ShouldNotBeNull();
    }

    private static ChecksPendingIssue CatchUpChecksPending() =>
        Builder().RebasePendingForCatchUp()
            .ResolveRebase(new RebaseOutcome.UpToDate(), DateTimeOffset.UtcNow)
            .ShouldBeOfType<ChecksPendingIssue>();

    [Fact]
    public async Task WhenRebasingAfterARun_ReasonIsAfterRunWithNoRound()
    {
        // Arrange
        _mergeQueue.Entry = new MergeQueueEntry(1, RoundLimit, null);

        // Act
        IssueStateDetails details = await DetailsOfAsync(Builder().RebasePending());

        // Assert
        details.ShouldSatisfyAllConditions(
            () => details.RebasePendingReason.ShouldBe("after_run"),
            () => details.CatchUpRound.ShouldBeNull(),
            () => details.CatchUpRoundLimit.ShouldBeNull(),
            () => details.MergeWait.ShouldBeNull());
    }

    [Fact]
    public async Task WhenRebasingForACatchUp_ReasonIsCatchUpWithTheEntrysRound()
    {
        // Arrange
        _mergeQueue.Entry = new MergeQueueEntry(1, RoundLimit, null);

        // Act
        IssueStateDetails details = await DetailsOfAsync(Builder().RebasePendingForCatchUp());

        // Assert
        details.ShouldSatisfyAllConditions(
            () => details.RebasePendingReason.ShouldBe("catch_up"),
            () => details.CatchUpRound.ShouldBe(1),
            () => details.CatchUpRoundLimit.ShouldBe(RoundLimit),
            () => details.MergeWait.ShouldBeNull());
        _mergeQueue.Reads.ShouldHaveSingleItem().ShouldBe((RepositoryId, IssueNumber));
    }

    [Fact]
    public async Task WhenCheckingAfterACatchUp_CarriesTheRound()
    {
        // Arrange
        _mergeQueue.Entry = new MergeQueueEntry(2, RoundLimit, null);

        // Act
        IssueStateDetails details = await DetailsOfAsync(CatchUpChecksPending());

        // Assert
        details.ShouldSatisfyAllConditions(
            () => details.ChecksPendingReason.ShouldBe("catch_up"),
            () => details.CatchUpRound.ShouldBe(2),
            () => details.CatchUpRoundLimit.ShouldBe(RoundLimit),
            () => details.RebasePendingReason.ShouldBeNull());
    }

    [Fact]
    public async Task WhenCheckingAfterARun_HasNoRoundAndReadsNoQueue()
    {
        // Arrange
        _mergeQueue.Entry = new MergeQueueEntry(1, RoundLimit, null);

        // Act
        IssueStateDetails details = await DetailsOfAsync(Builder().ChecksPending());

        // Assert
        details.CatchUpRound.ShouldBeNull();
        _mergeQueue.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenMergePendingAndWaiting_CarriesThePositionAndAwaitedIssue()
    {
        // Arrange
        _mergeQueue.Entry = new MergeQueueEntry(0, RoundLimit, new MergeQueueWait(3, 57));

        // Act
        IssueStateDetails details = await DetailsOfAsync(Builder().MergePending());

        // Assert
        details.ShouldSatisfyAllConditions(
            () => details.MergeWait.ShouldBe(new MergeWaitDetails(3, 57)),
            () => details.CatchUpRound.ShouldBeNull(),
            () => details.CatchUpRoundLimit.ShouldBeNull());
    }

    [Fact]
    public async Task WhenMergePendingAfterACatchUp_CarriesTheRoundWithoutAWait()
    {
        // Arrange
        _mergeQueue.Entry = new MergeQueueEntry(1, RoundLimit, null);

        // Act
        IssueStateDetails details = await DetailsOfAsync(Builder().MergePending());

        // Assert
        details.ShouldSatisfyAllConditions(
            () => details.MergeWait.ShouldBeNull(),
            () => details.CatchUpRound.ShouldBe(1),
            () => details.CatchUpRoundLimit.ShouldBe(RoundLimit));
    }

    [Fact]
    public async Task WhenMergePendingWithNoQueueRow_HasNoCatchUpFields()
    {
        // Arrange
        _mergeQueue.Entry = null;

        // Act
        IssueStateDetails details = await DetailsOfAsync(Builder().MergePending());

        // Assert
        details.ShouldSatisfyAllConditions(
            () => details.MergeWait.ShouldBeNull(),
            () => details.CatchUpRound.ShouldBeNull(),
            () => details.CatchUpRoundLimit.ShouldBeNull(),
            () => details.RebasePendingReason.ShouldBeNull());
    }

    [Fact]
    public async Task WhenInReview_ReadsNoQueue()
    {
        // Arrange
        _mergeQueue.Entry = new MergeQueueEntry(1, RoundLimit, new MergeQueueWait(1, null));

        // Act
        IssueStateDetails details = await DetailsOfAsync(Builder().Review());

        // Assert
        details.ShouldSatisfyAllConditions(
            () => details.MergeWait.ShouldBeNull(),
            () => details.CatchUpRound.ShouldBeNull());
        _mergeQueue.Reads.ShouldBeEmpty();
    }

    private sealed class StubMergeQueueQueries : IMergeQueueQueries
    {
        public MergeQueueEntry? Entry { get; set; }

        public List<(MonitoredRepositoryId RepositoryId, int IssueNumber)> Reads { get; } = [];

        public Task<MergeQueueEntry?> GetQueueEntryAsync(
            MonitoredRepositoryId repositoryId,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            Reads.Add((repositoryId, issueNumber));
            return Task.FromResult(Entry);
        }

        public Task<IReadOnlySet<(MonitoredRepositoryId RepositoryId, int IssueNumber)>> GetWaitingIssuesAsync(
            IReadOnlySet<MonitoredRepositoryId> repositoryIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<(MonitoredRepositoryId RepositoryId, int IssueNumber)>>(
                new HashSet<(MonitoredRepositoryId RepositoryId, int IssueNumber)>());
    }
}
