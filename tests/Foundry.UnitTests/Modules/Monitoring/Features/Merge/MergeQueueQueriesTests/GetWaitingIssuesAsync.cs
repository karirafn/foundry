using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Merge;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Merge.MergeQueueQueriesTests;

/// <summary>
/// The batch read of waiting catch-up queue entries for the board summaries (ADR 0111), over the persisted rows of
/// several repositories.
/// </summary>
public sealed class GetWaitingIssuesAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset Episode = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly MonitoredRepository _repository;
    private readonly MonitoredRepository _otherRepository;
    private readonly MergeQueueQueries _sut;

    public GetWaitingIssuesAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();

        _repository = CreateRepository("owner/repo");
        _otherRepository = CreateRepository("owner/other");
        _otherRepository.SetPosition(1);
        _dbContext.Set<MonitoredRepository>().AddRange(_repository, _otherRepository);
        _dbContext.SaveChanges();

        IssueQueries issueQueries = new(
            _dbContext,
            new NullRepositorySlugQueries(),
            new NullRepositoryEligibilityQuery(),
            new NullWorkerRunQueries(),
            new InFlightWorkerCountQuery(_dbContext, new NullWorkerRunQueries()),
            new NullGlobalSettingsQueries(),
            new NullMergeQueueQueries(),
            new NullRepositoryLifecycleSettingsQueries());
        _sut = new MergeQueueQueries(_dbContext, () => issueQueries);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static MonitoredRepository CreateRepository(string slug) =>
        MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            "github.com",
            null,
            rebaseAfterRun: true,
            mergeWhenChecksPass: true).ValueOrThrow();

    private void SeedMergePendingIssue(MonitoredRepository repository, int issueNumber)
    {
        _dbContext.Set<Issue>().Add(
            new IssueBuilder().WithMonitoredRepositoryId(repository.Id).WithIssueNumber(issueNumber).MergePending());
    }

    private void SeedWaitingRow(MonitoredRepository repository, int issueNumber)
    {
        PendingMerge row = PendingMerge.Start(repository.Id, issueNumber, Episode);
        MergeQueue.RefuseBehind(row, Episode);
        _dbContext.Set<PendingMerge>().Add(row);
    }

    private void SeedAttemptedRow(MonitoredRepository repository, int issueNumber)
    {
        _dbContext.Set<PendingMerge>().Add(PendingMerge.Start(repository.Id, issueNumber, Episode));
    }

    private void SeedResolvedRow(MonitoredRepository repository, int issueNumber)
    {
        PendingMerge row = PendingMerge.Start(repository.Id, issueNumber, Episode);
        row.MarkResolved(Episode);
        _dbContext.Set<PendingMerge>().Add(row);
    }

    private async Task SaveAsync()
    {
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ReturnsTheWaitingEntriesOfEveryRequestedRepository()
    {
        // Arrange
        SeedMergePendingIssue(_repository, 41);
        SeedWaitingRow(_repository, 41);
        SeedMergePendingIssue(_repository, 42);
        SeedAttemptedRow(_repository, 42);
        SeedMergePendingIssue(_repository, 43);
        SeedResolvedRow(_repository, 43);
        SeedMergePendingIssue(_otherRepository, 41);
        SeedWaitingRow(_otherRepository, 41);
        await SaveAsync();

        // Act
        IReadOnlySet<(MonitoredRepositoryId RepositoryId, int IssueNumber)> waiting = await _sut.GetWaitingIssuesAsync(
            new HashSet<MonitoredRepositoryId> { _repository.Id, _otherRepository.Id },
            TestContext.Current.CancellationToken);

        // Assert
        waiting.ShouldBe([(_repository.Id, 41), (_otherRepository.Id, 41)], ignoreOrder: true);
    }

    [Fact]
    public async Task LeavesOutRepositoriesNotRequested()
    {
        // Arrange
        SeedMergePendingIssue(_repository, 41);
        SeedWaitingRow(_repository, 41);
        SeedMergePendingIssue(_otherRepository, 42);
        SeedWaitingRow(_otherRepository, 42);
        await SaveAsync();

        // Act
        IReadOnlySet<(MonitoredRepositoryId RepositoryId, int IssueNumber)> waiting = await _sut.GetWaitingIssuesAsync(
            new HashSet<MonitoredRepositoryId> { _otherRepository.Id },
            TestContext.Current.CancellationToken);

        // Assert
        waiting.ShouldBe([(_otherRepository.Id, 42)]);
    }

    [Fact]
    public async Task WhenNoRowWaits_ReturnsNothing()
    {
        // Arrange
        SeedMergePendingIssue(_repository, 41);
        SeedAttemptedRow(_repository, 41);
        await SaveAsync();

        // Act
        IReadOnlySet<(MonitoredRepositoryId RepositoryId, int IssueNumber)> waiting = await _sut.GetWaitingIssuesAsync(
            new HashSet<MonitoredRepositoryId> { _repository.Id },
            TestContext.Current.CancellationToken);

        // Assert
        waiting.ShouldBeEmpty();
    }
}
