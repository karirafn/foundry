using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Merge;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.Merge;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Merge.MergeStepTests;

/// <summary>
/// Tests for the merge step (ADR 0105): each test drives one path from a merge-pending issue to a
/// <see cref="MergeResolved"/> outcome, a close event, or a retry.
/// </summary>
public sealed class RunAsync : IAsyncDisposable
{
    private const string PrUrl = "https://github.com/owner/repo/pull/99";
    private const string VerifiedHead = "1111111111111111111111111111111111111111";
    private const int IssueNumber = 42;

    private static readonly DateTimeOffset EnteredAt = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = EnteredAt.AddSeconds(5);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventDispatcher _dispatcher = new();
    private readonly WebhookNudgeQueue _nudgeQueue = new(NullLogger<WebhookNudgeQueue>.Instance);
    private readonly ScriptedProvider _provider = new();

    public RunAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private MonitoredRepository SeedRepository(bool mergeWhenChecksPass = true)
    {
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "github.com",
            null,
            mergeWhenChecksPass: mergeWhenChecksPass).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repository);
        _dbContext.SaveChanges();
        return repository;
    }

    private MergeStep BuildSut(params MergePendingIssueInfo[] pending) =>
        new(
            new StubIssueQueries(pending.Length == 0 ? [new MergePendingIssueInfo(IssueNumber, PrUrl, VerifiedHead, EnteredAt)] : pending),
            _dbContext,
            _dispatcher,
            _nudgeQueue,
            NullLogger<MergeStep>.Instance);

    private MergeResolved SingleResolved() =>
        _dispatcher.DispatchedEvents.OfType<MergeResolved>().ShouldHaveSingleItem();

    private PendingMerge LoadProgress()
    {
        _dbContext.ChangeTracker.Clear();
        return _dbContext.Set<PendingMerge>().Single();
    }

    private List<Nudge> DrainNudges()
    {
        List<Nudge> nudges = [];
        while (_nudgeQueue.Reader.TryRead(out Nudge nudge))
        {
            nudges.Add(nudge);
        }

        return nudges;
    }

    [Fact]
    public async Task WhenSettingIsOff_ResolvesDisabledWithoutProviderCalls()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository(mergeWhenChecksPass: false);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.RepositoryId.ShouldBe(repository.Id),
            () => resolved.IssueNumber.ShouldBe(IssueNumber),
            () => resolved.Outcome.ShouldBe("disabled"),
            () => resolved.Reason.ShouldBeNull());
        _provider.IsClosedCalls.ShouldBe(0);
        _provider.MergeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenProviderMerges_ResolvesMergedPinnedToTheVerifiedHead()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("merged");
        resolved.Reason.ShouldBeNull();
        _provider.MergeCalls.ShouldHaveSingleItem().ShouldBe((PrUrl, VerifiedHead));
        LoadProgress().IsResolved.ShouldBeTrue();
    }

    // MergeRequestOutcome is internal, so the theory names each provider answer and builds it here.
    private static MergeRequestOutcome Refusal(string variant) =>
        variant switch
        {
            nameof(MergeRequestOutcome.HeadMoved) => new MergeRequestOutcome.HeadMoved(),
            nameof(MergeRequestOutcome.NotMergeable) => new MergeRequestOutcome.NotMergeable("Pull Request is not mergeable"),
            nameof(MergeRequestOutcome.Unauthorized) => new MergeRequestOutcome.Unauthorized(),
            nameof(MergeRequestOutcome.Unsupported) => new MergeRequestOutcome.Unsupported(),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
        };

    [Theory]
    [InlineData(nameof(MergeRequestOutcome.HeadMoved), "head_moved")]
    [InlineData(nameof(MergeRequestOutcome.NotMergeable), "not_mergeable")]
    [InlineData(nameof(MergeRequestOutcome.Unauthorized), "unauthorized")]
    [InlineData(nameof(MergeRequestOutcome.Unsupported), "not_mergeable")]
    public async Task WhenProviderRefuses_ResolvesSkippedWithReason(string variant, string expectedReason)
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(Refusal(variant)));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe(expectedReason);
    }

    [Fact]
    public async Task WhenIssueClosed_RecordsProviderIssueClosedWithoutMerging()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.IssueClosed = true;

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        ProviderIssueClosed closed = _dispatcher.DispatchedEvents.ShouldHaveSingleItem().ShouldBeOfType<ProviderIssueClosed>();
        closed.IssueNumber.ShouldBe(IssueNumber);
        _provider.MergeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenPullRequestMergedElsewhere_ResolvesMergedWithoutMerging()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.PullRequestStatus = new PullRequestStatus(IsClosed: true, IsMerged: true, HeadSha: VerifiedHead);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("merged");
        _provider.MergeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenPullRequestClosedWithoutMerge_RecordsProviderPullRequestClosed()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.PullRequestStatus = new PullRequestStatus(IsClosed: true, IsMerged: false, HeadSha: VerifiedHead);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldHaveSingleItem().ShouldBeOfType<ProviderPullRequestClosed>()
            .IssueNumber.ShouldBe(IssueNumber);
        _provider.MergeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenMergeFailsTransiently_CountsTheFailureAndRetriesOnANotBeforeNudge()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Fail(new Error("GitHub.ServerError", "502 Bad Gateway")));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        PendingMerge progress = LoadProgress();
        progress.ShouldSatisfyAllConditions(
            () => progress.TransientFailures.ShouldBe(1),
            () => progress.IsResolved.ShouldBeFalse(),
            () => progress.NextAttemptAt.ShouldNotBeNull().ShouldBeGreaterThan(Now));
        Nudge nudge = DrainNudges().ShouldHaveSingleItem();
        nudge.ShouldSatisfyAllConditions(
            () => nudge.Key.Pass.ShouldBe(NudgePass.Merge),
            () => nudge.Key.IssueNumber.ShouldBe(IssueNumber),
            () => nudge.NotBefore.ShouldBe(progress.NextAttemptAt));
    }

    [Fact]
    public async Task WhenMergeThrowsHttpRequestException_CountsATransientFailure()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.ThrowOnMerge = new HttpRequestException("connection reset");

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        LoadProgress().TransientFailures.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTransientFailuresReachTheBudget_ResolvesSkippedProviderError()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        DateTimeOffset now = Now;
        for (int attempt = 0; attempt < PendingMerge.MaxTransientFailures; attempt++)
        {
            _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Fail(new Error("GitHub.ServerError", "502 Bad Gateway")));
        }

        // Act
        for (int attempt = 0; attempt < PendingMerge.MaxTransientFailures; attempt++)
        {
            await BuildSut().RunAsync(repository, _provider, null, now, TestContext.Current.CancellationToken);
            now = now.AddHours(1);
        }

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe("provider_error");
        _provider.MergeCalls.Count.ShouldBe(PendingMerge.MaxTransientFailures);
    }

    [Fact]
    public async Task WhenNotYetDue_SchedulesNudgeWithoutProviderCalls()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Fail(new Error("GitHub.ServerError", "502 Bad Gateway")));
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        DrainNudges();
        int isClosedCalls = _provider.IsClosedCalls;

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now.AddSeconds(1), TestContext.Current.CancellationToken);

        // Assert
        _provider.IsClosedCalls.ShouldBe(isClosedCalls);
        DrainNudges().ShouldHaveSingleItem().Key.Pass.ShouldBe(NudgePass.Merge);
    }

    [Fact]
    public async Task WhenAlreadyResolved_DoesNotMergeAgain()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        _provider.MergeCalls.ShouldHaveSingleItem();
        SingleResolved();
    }

    [Fact]
    public async Task WhenNewEpisodeStarts_ResetsTheProgress()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await BuildSut(new MergePendingIssueInfo(IssueNumber, PrUrl, VerifiedHead, EnteredAt.AddHours(1)))
            .RunAsync(repository, _provider, null, Now.AddHours(1), TestContext.Current.CancellationToken);

        // Assert
        _provider.MergeCalls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenNudgedForAnotherIssue_LeavesThisIssueAlone()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();

        // Act
        await BuildSut().RunAsync(repository, _provider, IssueNumber + 1, Now, TestContext.Current.CancellationToken);

        // Assert
        _provider.IsClosedCalls.ShouldBe(0);
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssueLeftMergePending_ForgetsItsProgress()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await new MergeStep(new StubIssueQueries([]), _dbContext, _dispatcher, _nudgeQueue, NullLogger<MergeStep>.Instance)
            .RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        _dbContext.Set<PendingMerge>().ShouldBeEmpty();
    }

    private sealed class StubIssueQueries(
        IReadOnlyList<MergePendingIssueInfo> mergePendingIssues) : IIssueQueries
    {
        public Task<IReadOnlyList<AwaitingInputIssueInfo>> GetAwaitingInputIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AwaitingInputIssueInfo>>([]);

        public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
            MonitoredRepositoryId repositoryId,
            IReadOnlySet<int> issueNumbers,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<int, IssueSnapshot>>(new Dictionary<int, IssueSnapshot>());

        public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);

        public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ReviewIssueInfo>>([]);

        public Task<IReadOnlyList<MergePendingIssueInfo>> GetMergePendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(mergePendingIssues);

        public Task<IReadOnlyList<RebasePendingIssueInfo>> GetRebasePendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RebasePendingIssueInfo>>([]);

        public Task<IReadOnlyList<ChecksPendingIssueInfo>> GetChecksPendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChecksPendingIssueInfo>>([]);

        public Task<IReadOnlySet<int>> GetUnpublishedRunIssueNumbersAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlySet<int>> GetDispatchCandidateIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IssueSummary>>([]);

        public Task<IssueSummary?> GetIssueSummaryAsync(
            IssueId issueId,
            CancellationToken cancellationToken)
            => Task.FromResult<IssueSummary?>(null);

        public Task<Result<IssueDetail>> GetIssueDetailAsync(
            IssueId issueId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IssueDetail>.Fail(IssueErrors.NotFound(issueId)));

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

    private sealed class ScriptedProvider : IIssueProvider
    {
        public Queue<Result<MergeRequestOutcome>> Merges { get; } = new();

        public List<(string PullRequestUrl, string ExpectedHeadSha)> MergeCalls { get; } = [];

        public int IsClosedCalls { get; private set; }

        public bool IssueClosed { get; set; }

        public PullRequestStatus PullRequestStatus { get; set; } = new(IsClosed: false, IsMerged: false, HeadSha: VerifiedHead);

        public Exception? ThrowOnMerge { get; set; }

        public Task<Result<MergeRequestOutcome>> MergePullRequestAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            string expectedHeadSha,
            CancellationToken cancellationToken)
        {
            MergeCalls.Add((pullRequestUrl, expectedHeadSha));
            if (ThrowOnMerge is { } exception)
            {
                throw exception;
            }

            return Task.FromResult(Merges.Dequeue());
        }

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            IsClosedCalls++;
            return Task.FromResult(Result<bool>.Ok(IssueClosed));
        }

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<PullRequestStatus>.Ok(PullRequestStatus));

        public Task<Result<IssueListing>> GetIssuesAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            PostedCommentIds postedComments,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result> ApplyBranchRulesAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<bool>> CreateBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<bool>> DeleteBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<Checks>> GetChecksAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _events = [];

        public IReadOnlyList<IIntegrationEvent> DispatchedEvents => _events;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _events.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
