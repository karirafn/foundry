using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetMergeQueueIssuesAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;
    private readonly MonitoredRepositoryId _repositoryId = MonitoredRepositoryId.New();

    public GetMergeQueueIssuesAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new IssueQueries(
            _dbContext,
            new NullRepositorySlugQueries(),
            new NullRepositoryEligibilityQuery(),
            new NullWorkerRunQueries(),
            new InFlightWorkerCountQuery(_dbContext, new NullWorkerRunQueries()),
            new NullGlobalSettingsQueries(),
            new NullMergeQueueQueries(),
            new NullRepositoryLifecycleSettingsQueries());
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private IssueBuilder Issue(int number) =>
        new IssueBuilder()
            .WithMonitoredRepositoryId(_repositoryId)
            .WithIssueNumber(number)
            .WithBranchName($"feat/{number}-thing");

    private void Seed(params Issue[] issues)
    {
        _dbContext.Set<Issue>().AddRange(issues);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ReturnsGateAndMergeIssuesAndCatchUpRebasesOnly()
    {
        // Arrange
        Seed(
            Issue(1).ChecksPending(),
            Issue(2).MergePending(),
            Issue(3).RebasePendingForCatchUp(),
            Issue(4).RebasePending(),
            Issue(5).RebasePendingForCatchUp()
                .ResolveRebase(new RebaseOutcome.Rebased(), DateTimeOffset.UtcNow),
            Issue(6).Review(),
            new IssueBuilder().WithMonitoredRepositoryId(MonitoredRepositoryId.New()).WithIssueNumber(7).MergePending());

        // Act
        IReadOnlyList<MergeQueueIssueInfo> issues = await _sut.GetMergeQueueIssuesAsync(
            _repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        issues.OrderBy(i => i.IssueNumber).ShouldBe(
        [
            new MergeQueueIssueInfo(1, "feat/1-thing", false, false),
            new MergeQueueIssueInfo(2, "feat/2-thing", false, false),
            new MergeQueueIssueInfo(3, "feat/3-thing", true, false),
            new MergeQueueIssueInfo(5, "feat/5-thing", true, false),
        ]);
    }

    [Fact]
    public async Task CarriesPersonFeedback()
    {
        // Arrange
        Seed(Issue(1).ChecksPending().Revise([new ReviewComment("Rename it.")], DateTimeOffset.UtcNow)
            .Claim(Foundry.Modules.Workers.Contracts.WorkerRunId.New(), DateTimeOffset.UtcNow)
            .MarkRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            .MarkChecksPending(new RebaseOutcome.UpToDate(), DateTimeOffset.UtcNow));

        // Act
        IReadOnlyList<MergeQueueIssueInfo> issues = await _sut.GetMergeQueueIssuesAsync(
            _repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        issues.ShouldHaveSingleItem().HasPersonFeedback.ShouldBeTrue();
    }

    [Fact]
    public async Task MergePendingIssuesCarryTheirBranch()
    {
        // Arrange
        Seed(Issue(2).MergePending());

        // Act
        IReadOnlyList<MergePendingIssueInfo> issues = await _sut.GetMergePendingIssuesAsync(
            _repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        issues.ShouldHaveSingleItem().BranchName.ShouldBe("feat/2-thing");
    }
}
