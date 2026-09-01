using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.StateChanges;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetResolvedIssueSummariesAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    private static readonly DateTimeOffset BaseTime = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    public GetResolvedIssueSummariesAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new IssueQueries(_dbContext, new NullRepositorySlugQueries(), new NullRepositoryEligibilityQuery(), new NullWorkerRunQueries());
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private CompletedIssue SeedCompletedIssue(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset detectedAt)
    {
        CompletedIssue completed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(detectedAt)
            .WithFeedbackCutoffAt(detectedAt.AddHours(1))
            .WithCompletedAt(detectedAt.AddHours(2))
            .Completed();
        _dbContext.Set<Issue>().Add(completed);
        _dbContext.SaveChanges();
        return completed;
    }

    private UnchangedIssue SeedUnchangedIssue(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset detectedAt)
    {
        UnchangedIssue unchanged = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(detectedAt)
            .Unchanged();
        _dbContext.Set<Issue>().Add(unchanged);
        _dbContext.SaveChanges();
        return unchanged;
    }

    private DetectedIssue SeedActiveIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(BaseTime)
            .Detected();
        _dbContext.Set<Issue>().Add(detected);
        _dbContext.SaveChanges();
        return detected;
    }

    [Fact]
    public async Task WhenMoreRowsExist_FirstPageHasNonNullNextCursor()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedCompletedIssue(repoId, issueNumber: 1, detectedAt: BaseTime.AddHours(-2));
        SeedCompletedIssue(repoId, issueNumber: 2, detectedAt: BaseTime.AddHours(-1));
        SeedCompletedIssue(repoId, issueNumber: 3, detectedAt: BaseTime);

        // Act
        PagedIssues result = await _sut.GetResolvedIssueSummariesAsync(
            repositoryId: null,
            states: IssueStateRegistry.Resolved,
            cursor: null,
            limit: 2,
            TestContext.Current.CancellationToken);

        // Assert
        result.Items.Count.ShouldBe(2);
        result.NextCursor.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenFirstPage_ItemsOrderedByDetectedAtDescThenIdAsc()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedCompletedIssue(repoId, issueNumber: 1, detectedAt: BaseTime.AddHours(-2));
        SeedCompletedIssue(repoId, issueNumber: 2, detectedAt: BaseTime.AddHours(-1));
        SeedCompletedIssue(repoId, issueNumber: 3, detectedAt: BaseTime);

        // Act
        PagedIssues result = await _sut.GetResolvedIssueSummariesAsync(
            repositoryId: null,
            states: IssueStateRegistry.Resolved,
            cursor: null,
            limit: 3,
            TestContext.Current.CancellationToken);

        // Assert
        result.Items.Count.ShouldBe(3);
        result.Items[0].IssueNumber.ShouldBe(3);
        result.Items[1].IssueNumber.ShouldBe(2);
        result.Items[2].IssueNumber.ShouldBe(1);
    }

    [Fact]
    public async Task WhenFollowingNextCursor_ReturnsNextPageWithNoOverlapOrGap()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedCompletedIssue(repoId, issueNumber: 1, detectedAt: BaseTime.AddHours(-2));
        SeedCompletedIssue(repoId, issueNumber: 2, detectedAt: BaseTime.AddHours(-1));
        SeedCompletedIssue(repoId, issueNumber: 3, detectedAt: BaseTime);

        PagedIssues firstPage = await _sut.GetResolvedIssueSummariesAsync(
            repositoryId: null,
            states: IssueStateRegistry.Resolved,
            cursor: null,
            limit: 2,
            TestContext.Current.CancellationToken);

        // Act
        PagedIssues secondPage = await _sut.GetResolvedIssueSummariesAsync(
            repositoryId: null,
            states: IssueStateRegistry.Resolved,
            cursor: firstPage.NextCursor,
            limit: 2,
            TestContext.Current.CancellationToken);

        // Assert
        secondPage.Items.ShouldHaveSingleItem();
        secondPage.Items[0].IssueNumber.ShouldBe(1);
        secondPage.NextCursor.ShouldBeNull();

        // Verify no overlap
        IEnumerable<int> firstIssueNumbers = firstPage.Items.Select(i => i.IssueNumber);
        IEnumerable<int> secondIssueNumbers = secondPage.Items.Select(i => i.IssueNumber);
        firstIssueNumbers.Intersect(secondIssueNumbers).ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenLastPage_NextCursorIsNull()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedCompletedIssue(repoId, issueNumber: 1, detectedAt: BaseTime);

        // Act
        PagedIssues result = await _sut.GetResolvedIssueSummariesAsync(
            repositoryId: null,
            states: IssueStateRegistry.Resolved,
            cursor: null,
            limit: 10,
            TestContext.Current.CancellationToken);

        // Assert
        result.Items.ShouldHaveSingleItem();
        result.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task WhenNoResolvedIssues_ReturnsEmptyPagedIssues()
    {
        // Arrange — no seeded data

        // Act
        PagedIssues result = await _sut.GetResolvedIssueSummariesAsync(
            repositoryId: null,
            states: IssueStateRegistry.Resolved,
            cursor: null,
            limit: 10,
            TestContext.Current.CancellationToken);

        // Assert
        result.Items.ShouldBeEmpty();
        result.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task WhenRepositoryIdProvided_ReturnsOnlyMatchingRepoIssues()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();
        SeedCompletedIssue(targetRepo, issueNumber: 1, detectedAt: BaseTime);
        SeedCompletedIssue(otherRepo, issueNumber: 2, detectedAt: BaseTime.AddHours(-1));

        // Act
        PagedIssues result = await _sut.GetResolvedIssueSummariesAsync(
            repositoryId: targetRepo,
            states: IssueStateRegistry.Resolved,
            cursor: null,
            limit: 10,
            TestContext.Current.CancellationToken);

        // Assert
        result.Items.ShouldHaveSingleItem();
        result.Items[0].IssueNumber.ShouldBe(1);
    }

    [Fact]
    public async Task WhenLimitIsZero_UsesDefaultLimit()
    {
        // Arrange — seed 60 issues to exceed the default of 50
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        for (int i = 1; i <= 60; i++)
        {
            SeedCompletedIssue(repoId, issueNumber: i, detectedAt: BaseTime.AddMinutes(-i));
        }

        // Act
        PagedIssues result = await _sut.GetResolvedIssueSummariesAsync(
            repositoryId: null,
            states: IssueStateRegistry.Resolved,
            cursor: null,
            limit: 0,
            TestContext.Current.CancellationToken);

        // Assert — default is 50
        result.Items.Count.ShouldBe(50);
        result.NextCursor.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenLimitExceedsMax_ClampsToOneHundred()
    {
        // Arrange — seed 110 issues to exceed the cap of 100
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        for (int i = 1; i <= 110; i++)
        {
            SeedCompletedIssue(repoId, issueNumber: i, detectedAt: BaseTime.AddMinutes(-i));
        }

        // Act
        PagedIssues result = await _sut.GetResolvedIssueSummariesAsync(
            repositoryId: null,
            states: IssueStateRegistry.Resolved,
            cursor: null,
            limit: 500,
            TestContext.Current.CancellationToken);

        // Assert
        result.Items.Count.ShouldBe(100);
        result.NextCursor.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenActiveIssuesExist_ExcludesThemFromResolvedResult()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedCompletedIssue(repoId, issueNumber: 1, detectedAt: BaseTime);
        SeedActiveIssue(repoId, issueNumber: 2);

        // Act
        PagedIssues result = await _sut.GetResolvedIssueSummariesAsync(
            repositoryId: null,
            states: IssueStateRegistry.Resolved,
            cursor: null,
            limit: 10,
            TestContext.Current.CancellationToken);

        // Assert
        result.Items.ShouldHaveSingleItem();
        result.Items[0].IssueNumber.ShouldBe(1);
        result.Items[0].State.ShouldBe("completed");
    }

    [Fact]
    public async Task WhenExplicitCompletedState_ExcludesUnchangedIssues()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        SeedCompletedIssue(repoId, issueNumber: 1, detectedAt: BaseTime);
        SeedUnchangedIssue(repoId, issueNumber: 2, detectedAt: BaseTime.AddHours(-1));

        // Act — request only "completed" state; unchanged is active and excluded
        PagedIssues result = await _sut.GetResolvedIssueSummariesAsync(
            repositoryId: null,
            states: ["completed"],
            cursor: null,
            limit: 10,
            TestContext.Current.CancellationToken);

        // Assert
        result.Items.ShouldHaveSingleItem();
        result.Items[0].State.ShouldBe("completed");
    }
}
