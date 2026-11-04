using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.RetryIssueFreshHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const string WorkBranch = "feat/2-issue-2";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly RecordingWorkBranchCommands _branchCommands;
    private readonly RetryIssueFresh.Handler _sut;

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
        _branchCommands = new RecordingWorkBranchCommands();
        _sut = new RetryIssueFresh.Handler(
            _dbContext,
            _dispatcher,
            new StubIssueQueries(),
            _branchCommands,
            NullLogger<RetryIssueFresh.Handler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<T> SeedAsync<T>(T issue, CancellationToken cancellationToken)
        where T : Issue
    {
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    private Task<ContinuableFailedIssue> SeedContinuableFailedIssueAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken) =>
        SeedAsync(
            new IssueBuilder()
                .WithMonitoredRepositoryId(repositoryId)
                .WithIssueNumber(2)
                .WithBranchName(WorkBranch)
                .ContinuableFailed(),
            cancellationToken);

    private async Task<Issue?> ReloadAsync(IssueId id)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == id, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenContinuableFailed_DeletesTheBranchAndRequeuesFresh()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue failed = await SeedContinuableFailedIssueAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Act
        Result<IssueDetail> result = await _sut.HandleAsync(new RetryIssueFresh.Command(failed.Id), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Success>();
        _branchCommands.Deleted.ShouldHaveSingleItem().ShouldBe((repositoryId, WorkBranch));
        Issue? issue = await ReloadAsync(failed.Id);
        FreshQueuedIssue queued = issue.ShouldBeOfType<FreshQueuedIssue>();
        queued.Context.ShouldBeOfType<DispatchContext.Fresh>();
        _dispatcher.DispatchedEvents.OfType<IssueQueued>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenBranchWasAlreadyGone_StillRequeuesFresh()
    {
        // Arrange
        _branchCommands.Result = Result<bool>.Ok(false);
        ContinuableFailedIssue failed = await SeedContinuableFailedIssueAsync(
            MonitoredRepositoryId.New(),
            TestContext.Current.CancellationToken);

        // Act
        Result<IssueDetail> result = await _sut.HandleAsync(new RetryIssueFresh.Command(failed.Id), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Success>();
        (await ReloadAsync(failed.Id)).ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenBranchDeletionFails_ReturnsBranchDiscardFailedAndKeepsTheIssue()
    {
        // Arrange
        _branchCommands.Result = Result<bool>.Fail(new Error("GitHub.UnexpectedStatusCode", "GitHub API returned unexpected status code 403."));
        ContinuableFailedIssue failed = await SeedContinuableFailedIssueAsync(
            MonitoredRepositoryId.New(),
            TestContext.Current.CancellationToken);

        // Act
        Result<IssueDetail> result = await _sut.HandleAsync(new RetryIssueFresh.Command(failed.Id), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.BranchDiscardFailedCode);
        (await ReloadAsync(failed.Id)).ShouldBeOfType<ContinuableFailedIssue>();
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssueIsPlainFailed_ReturnsWrongStateWithoutTouchingBranches()
    {
        // Arrange
        FailedIssue failed = await SeedAsync(
            new IssueBuilder().WithIssueNumber(1).Failed(),
            TestContext.Current.CancellationToken);

        // Act
        Result<IssueDetail> result = await _sut.HandleAsync(new RetryIssueFresh.Command(failed.Id), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.WrongStateCode);
        _branchCommands.Deleted.ShouldBeEmpty();
        (await ReloadAsync(failed.Id)).ShouldBeOfType<FailedIssue>();
    }

    [Fact]
    public async Task WhenIssueNotFound_ReturnsNotFoundError()
    {
        // Act
        Result<IssueDetail> result = await _sut.HandleAsync(new RetryIssueFresh.Command(IssueId.New()), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.NotFoundCode);
        _branchCommands.Deleted.ShouldBeEmpty();
    }

    private sealed class RecordingWorkBranchCommands : IWorkBranchCommands
    {
        public List<(MonitoredRepositoryId RepositoryId, string BranchName)> Deleted { get; } = [];

        public Result<bool> Result { get; set; } = Result<bool>.Ok(true);

        public Task<Result<bool>> DeleteBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            Deleted.Add((repositoryId, branchName));
            return Task.FromResult(Result);
        }
    }

    private sealed class StubIssueQueries : IIssueQueries
    {
        public Task<Result<IssueDetail>> GetIssueDetailAsync(
            IssueId issueId,
            CancellationToken cancellationToken)
        {
            IssueDetail detail = new(
                Id: issueId.Value,
                IssueNumber: 1,
                Title: "stub",
                State: "queued",
                RepositorySlug: "owner/repo",
                ProviderType: "github",
                DetectedAt: DateTimeOffset.UtcNow,
                Url: "https://github.com/owner/repo/issues/1",
                Author: "octocat",
                Labels: [],
                StateDetails: null);
            return Task.FromResult(Result<IssueDetail>.Ok(detail));
        }

        public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
            MonitoredRepositoryId repositoryId,
            IReadOnlySet<int> issueNumbers,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<int, IssueSnapshot>>(new Dictionary<int, IssueSnapshot>());

        public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ReviewIssueInfo>>([]);

        public Task<IReadOnlyList<ChecksPendingIssueInfo>> GetChecksPendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChecksPendingIssueInfo>>([]);

        public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IssueSummary>>([]);

        public Task<IssueSummary?> GetIssueSummaryAsync(
            IssueId issueId,
            CancellationToken cancellationToken)
            => Task.FromResult<IssueSummary?>(null);

        public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);

        public Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlySet<int>> GetDispatchCandidateIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyList<IssueSummary>> GetActiveIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            IReadOnlyCollection<string>? states,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<PagedIssues> GetResolvedIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            IReadOnlyCollection<string> states,
            string? cursor,
            int limit,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<IssueStateCounts> GetIssueStateCountsAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }
}
