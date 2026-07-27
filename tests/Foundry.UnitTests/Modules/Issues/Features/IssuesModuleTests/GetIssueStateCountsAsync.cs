using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetIssueStateCountsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    private static readonly DateTimeOffset Now = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public GetIssueStateCountsAsync()
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

    private DetectedIssue SeedDetectedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        _dbContext.Set<Issue>().Add(detected);
        _dbContext.SaveChanges();
        return detected;
    }

    private CompletedIssue SeedCompletedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            "feat/1-fix",
            "https://github.com/owner/repo/pull/10",
            Now);
        CompletedIssue completed = review.Complete(Now);
        _dbContext.Set<Issue>().Add(completed);
        _dbContext.SaveChanges();
        return completed;
    }

    private UnchangedIssue SeedUnchangedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        UnchangedIssue unchanged = inProgress.MarkUnchanged(Guid.NewGuid());
        _dbContext.Set<Issue>().Add(unchanged);
        _dbContext.SaveChanges();
        return unchanged;
    }

    [Fact]
    public async Task WhenMixedStatesSeed_PerStateCountsAreAccurate()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 1);
        SeedDetectedIssue(repositoryId, issueNumber: 2);
        SeedDetectedIssue(repositoryId, issueNumber: 3);
        SeedCompletedIssue(repositoryId, issueNumber: 4);
        SeedCompletedIssue(repositoryId, issueNumber: 5);
        SeedUnchangedIssue(repositoryId, issueNumber: 6);

        // Act
        IssueStateCounts result = await _sut.GetIssueStateCountsAsync(
            repositoryId: null,
            TestContext.Current.CancellationToken);

        // Assert
        result.Counts["detected"].ShouldBe(3);
        result.Counts["completed"].ShouldBe(2);
        result.Counts["unchanged"].ShouldBe(1);
    }

    [Fact]
    public async Task WhenSomeStatesHaveNoRows_ThoseStatesAreZeroInResult()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 1);

        // Act
        IssueStateCounts result = await _sut.GetIssueStateCountsAsync(
            repositoryId: null,
            TestContext.Current.CancellationToken);

        // Assert
        result.Counts["queued"].ShouldBe(0);
        result.Counts["blocked"].ShouldBe(0);
        result.Counts["in_progress"].ShouldBe(0);
        result.Counts["review"].ShouldBe(0);
        result.Counts["failed"].ShouldBe(0);
        result.Counts["continuable_failed"].ShouldBe(0);
        result.Counts["continuation_queued"].ShouldBe(0);
        result.Counts["revision_queued"].ShouldBe(0);
        result.Counts["revision_in_progress"].ShouldBe(0);
        result.Counts["revision_failed"].ShouldBe(0);
        result.Counts["completed"].ShouldBe(0);
        result.Counts["unchanged"].ShouldBe(0);
    }

    [Fact]
    public async Task WhenAllKnownStatesRequested_AllThirteenStatesAreKeys()
    {
        // Arrange — no seeded data; all counts should be zero

        // Act
        IssueStateCounts result = await _sut.GetIssueStateCountsAsync(
            repositoryId: null,
            TestContext.Current.CancellationToken);

        // Assert
        IReadOnlySet<string> allKnownStates = IssueStateRegistry.Active
            .Concat(IssueStateRegistry.Resolved)
            .ToHashSet();

        result.Counts.Count.ShouldBe(13);
        foreach (string state in allKnownStates)
        {
            result.Counts.ShouldContainKey(state);
        }
    }

    [Fact]
    public async Task WhenRepositoryIdProvided_CountsReflectOnlyMatchingRepo()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();
        SeedDetectedIssue(targetRepo, issueNumber: 1);
        SeedDetectedIssue(targetRepo, issueNumber: 2);
        SeedDetectedIssue(otherRepo, issueNumber: 3);
        SeedCompletedIssue(otherRepo, issueNumber: 4);

        // Act
        IssueStateCounts result = await _sut.GetIssueStateCountsAsync(
            repositoryId: targetRepo,
            TestContext.Current.CancellationToken);

        // Assert
        result.Counts["detected"].ShouldBe(2);
        result.Counts["completed"].ShouldBe(0);
    }
}
