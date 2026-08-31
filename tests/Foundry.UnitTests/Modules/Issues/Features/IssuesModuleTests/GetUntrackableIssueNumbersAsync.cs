using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetUntrackableIssueNumbersAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    private static readonly DateTimeOffset Now = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    public GetUntrackableIssueNumbersAsync()
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
        DetectedIssue detected = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .Detected();
        _dbContext.Set<Issue>().Add(detected);
        _dbContext.SaveChanges();
        return detected;
    }

    private CompletedIssue SeedCompletedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        CompletedIssue completed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .WithFeedbackCutoffAt(Now)
            .WithCompletedAt(Now)
            .Completed();
        _dbContext.Set<Issue>().Add(completed);
        _dbContext.SaveChanges();
        return completed;
    }

    private FreshQueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        return queued;
    }

    private BlockedIssue SeedBlockedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        BlockedIssue blocked = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .FreshQueued()
            .Block([99]);
        _dbContext.Set<Issue>().Add(blocked);
        _dbContext.SaveChanges();
        return blocked;
    }

    private FailedIssue SeedFailedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        FailedIssue failed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .WithFailedAt(Now)
            .Failed();
        _dbContext.Set<Issue>().Add(failed);
        _dbContext.SaveChanges();
        return failed;
    }

    private ContinuableFailedIssue SeedContinuableFailedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        ContinuableFailedIssue continuableFailed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .WithFailedAt(Now)
            .ContinuableFailed();
        _dbContext.Set<Issue>().Add(continuableFailed);
        _dbContext.SaveChanges();
        return continuableFailed;
    }

    private RevisionQueuedIssue SeedRevisionQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .RevisionQueued();
        _dbContext.Set<Issue>().Add(revisionQueued);
        _dbContext.SaveChanges();
        return revisionQueued;
    }

    private RevisionFailedIssue SeedRevisionFailedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        RevisionFailedIssue revisionFailed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .WithFailedAt(Now)
            .RevisionFailed();
        _dbContext.Set<Issue>().Add(revisionFailed);
        _dbContext.SaveChanges();
        return revisionFailed;
    }

    private ContinuationQueuedIssue SeedContinuationQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        ContinuationQueuedIssue continuationQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .ContinuableFailed()
            .Retry();
        _dbContext.Set<Issue>().Add(continuationQueued);
        _dbContext.SaveChanges();
        return continuationQueued;
    }

    private InProgressIssue SeedInProgressIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .InProgress();
        _dbContext.Set<Issue>().Add(inProgress);
        _dbContext.SaveChanges();
        return inProgress;
    }

    private ReviewIssue SeedReviewIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        ReviewIssue review = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .Review();
        _dbContext.Set<Issue>().Add(review);
        _dbContext.SaveChanges();
        return review;
    }

    private RevisionInProgressIssue SeedRevisionInProgressIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .RevisionInProgress();
        _dbContext.Set<Issue>().Add(revisionInProgress);
        _dbContext.SaveChanges();
        return revisionInProgress;
    }

    private UnchangedIssue SeedUnchangedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        UnchangedIssue unchanged = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .Unchanged();
        _dbContext.Set<Issue>().Add(unchanged);
        _dbContext.SaveChanges();
        return unchanged;
    }

    [Fact]
    public async Task WhenNoIssuesExist_ReturnsEmptySet()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenDetectedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 1);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([1], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenCompletedIssueExists_DoesNotReturnItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedCompletedIssue(repositoryId, issueNumber: 10);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenInProgressIssueExists_DoesNotReturnItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedInProgressIssue(repositoryId, issueNumber: 5);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenMixOfRestingAndPreservedIssuesExist_ReturnsOnlyRestingStateNumbers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 1);
        SeedCompletedIssue(repositoryId, issueNumber: 2);
        SeedInProgressIssue(repositoryId, issueNumber: 3);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([1], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenIssuesExistForDifferentRepository_ReturnsOnlyMatchingNumbers()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();

        SeedDetectedIssue(targetRepo, issueNumber: 10);
        SeedDetectedIssue(otherRepo, issueNumber: 20);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            targetRepo,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([10], ignoreOrder: true);
    }

    // Resting state coverage — each of the eight resting states must be returned by the query.

    [Fact]
    public async Task WhenQueuedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId, issueNumber: 2);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([2], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenBlockedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedBlockedIssue(repositoryId, issueNumber: 3);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([3], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenFailedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedFailedIssue(repositoryId, issueNumber: 4);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([4], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenContinuableFailedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedContinuableFailedIssue(repositoryId, issueNumber: 5);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([5], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenRevisionQueuedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRevisionQueuedIssue(repositoryId, issueNumber: 6);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([6], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenRevisionFailedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRevisionFailedIssue(repositoryId, issueNumber: 7);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([7], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenContinuationQueuedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedContinuationQueuedIssue(repositoryId, issueNumber: 8);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([8], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenUnchangedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedUnchangedIssue(repositoryId, issueNumber: 22);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([22], ignoreOrder: true);
    }

    // Preserved state coverage — active and terminal states must NOT be returned.

    [Fact]
    public async Task WhenReviewIssueExists_DoesNotReturnItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssue(repositoryId, issueNumber: 20);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRevisionInProgressIssueExists_DoesNotReturnItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRevisionInProgressIssue(repositoryId, issueNumber: 21);

        // Act
        IReadOnlySet<int> result = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    // Cross-reference test: the query's resting-state set and Issue.IsRestingState() must agree
    // across all known issue types, so future state additions that update one but not the other
    // fail here.
    [Fact]
    public async Task QueryRestingSetAndDomainPredicateAgreeAcrossAllIssueStates()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Seed one issue in every known state, each with a unique issue number.
        SeedDetectedIssue(repositoryId, issueNumber: 101);
        SeedQueuedIssue(repositoryId, issueNumber: 102);
        SeedBlockedIssue(repositoryId, issueNumber: 103);
        SeedFailedIssue(repositoryId, issueNumber: 104);
        SeedContinuableFailedIssue(repositoryId, issueNumber: 105);
        SeedRevisionQueuedIssue(repositoryId, issueNumber: 106);
        SeedRevisionFailedIssue(repositoryId, issueNumber: 107);
        SeedContinuationQueuedIssue(repositoryId, issueNumber: 108);
        SeedInProgressIssue(repositoryId, issueNumber: 109);
        SeedRevisionInProgressIssue(repositoryId, issueNumber: 110);
        SeedReviewIssue(repositoryId, issueNumber: 111);
        SeedUnchangedIssue(repositoryId, issueNumber: 112);
        SeedCompletedIssue(repositoryId, issueNumber: 113);

        // Act
        IReadOnlySet<int> queryResult = await _sut.GetUntrackableIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        List<Issue> allIssues = _dbContext.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .ToList();

        HashSet<int> domainPredicateResult = allIssues
            .Where(i => i.IsRestingState())
            .Select(i => i.IssueNumber)
            .ToHashSet();

        // Assert — both sets must match exactly; drift between the EF query and the domain predicate
        // means a state would be incorrectly included or excluded on the irreversible delete path.
        queryResult.ShouldBe(domainPredicateResult, ignoreOrder: true);
    }
}
