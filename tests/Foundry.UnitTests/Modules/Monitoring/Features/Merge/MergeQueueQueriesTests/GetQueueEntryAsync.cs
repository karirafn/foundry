using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
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
/// The catch-up queue entry read for the issue detail (ADR 0111), over the persisted rows and the Issues module's
/// merge-path issues.
/// </summary>
public sealed class GetQueueEntryAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset Episode = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly MonitoredRepository _repository;
    private readonly MergeQueueQueries _sut;

    public GetQueueEntryAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();

        _repository = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "github.com",
            null,
            rebaseAfterRun: true,
            mergeWhenChecksPass: true).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(_repository);
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

    private void SeedMergePendingIssue(int issueNumber)
    {
        _dbContext.Set<Issue>().Add(
            new IssueBuilder().WithMonitoredRepositoryId(_repository.Id).WithIssueNumber(issueNumber).MergePending());
    }

    private void SeedWaitingRow(int issueNumber, DateTimeOffset queuedAt)
    {
        PendingMerge row = PendingMerge.Start(_repository.Id, issueNumber, Episode);
        MergeQueue.RefuseBehind(row, queuedAt);
        _dbContext.Set<PendingMerge>().Add(row);
    }

    private async Task SaveAsync()
    {
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task WhenTheIssueHasNoRow_ReturnsNull()
    {
        // Arrange
        SeedMergePendingIssue(41);
        SeedWaitingRow(41, Episode);
        await SaveAsync();

        // Act
        MergeQueueEntry? entry = await _sut.GetQueueEntryAsync(_repository.Id, 42, TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBeNull();
    }

    [Fact]
    public async Task WhenWaiting_CountsItsPlaceAmongQueuedRowsOfHeldIssues()
    {
        // Arrange: issue 40's row is stale (the issue left the merge path); 41 joined before 42.
        SeedWaitingRow(40, Episode.AddMinutes(-5));
        SeedMergePendingIssue(41);
        SeedWaitingRow(41, Episode);
        SeedMergePendingIssue(42);
        SeedWaitingRow(42, Episode.AddMinutes(1));
        await SaveAsync();

        // Act
        MergeQueueEntry? entry = await _sut.GetQueueEntryAsync(_repository.Id, 42, TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBe(new MergeQueueEntry(0, MergeQueue.MaxCatchUpRounds, new MergeQueueWait(2, null)));
    }

    [Fact]
    public async Task WhenNotQueued_ReturnsAnEntryWithoutAWait()
    {
        // Arrange
        SeedMergePendingIssue(41);
        _dbContext.Set<PendingMerge>().Add(PendingMerge.Start(_repository.Id, 41, Episode));
        await SaveAsync();

        // Act
        MergeQueueEntry? entry = await _sut.GetQueueEntryAsync(_repository.Id, 41, TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBe(new MergeQueueEntry(0, MergeQueue.MaxCatchUpRounds, null));
    }
}
