using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Polling.RepositoryPollerTests;

/// <summary>
/// Tests for the checks-pending gate pass (Pass 5) in <see cref="RepositoryPoller.PollAsync"/>.
/// Each test exercises one cell of the decision table: (verdict, elapsed) → event or stay.
/// </summary>
public sealed class ChecksPendingPollAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventDispatcher _dispatcher;
    private readonly NullEligibilityEvaluator _eligibilityEvaluator;

    private static readonly DateTimeOffset EnteredAt = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = EnteredAt.AddMinutes(10);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    public ChecksPendingPollAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _dispatcher = new CapturingIntegrationEventDispatcher();
        _eligibilityEvaluator = new NullEligibilityEvaluator();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private MonitoredRepository SeedRepository()
    {
        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, "github.com", null).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repository);
        _dbContext.SaveChanges();
        return repository;
    }

    private RepositoryPoller BuildPoller(IIssueQueries issueQueries)
    {
        return new RepositoryPoller(
            issueQueries,
            _dbContext,
            new NullDomainEventDispatcher(),
            _dispatcher,
            _eligibilityEvaluator,
            NullLogger<RepositoryPoller>.Instance);
    }

    private static ChecksPendingIssueInfo BuildChecksPendingIssue(
        int issueNumber = 42,
        string pullRequestUrl = "https://github.com/owner/repo/pull/99",
        DateTimeOffset? enteredChecksPendingAt = null,
        DateTimeOffset? feedbackCutoffAt = null)
    {
        return new ChecksPendingIssueInfo(
            IssueNumber: issueNumber,
            PullRequestUrl: pullRequestUrl,
            EnteredChecksPendingAt: enteredChecksPendingAt ?? EnteredAt,
            FeedbackCutoffAt: feedbackCutoffAt ?? Now.AddDays(7));
    }

    // --- Green verdict → "passed" ---

    [Fact]
    public async Task WhenChecksGreen_RecordsChecksVerdictResolvedWithOutcomePassed()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue();
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            checksResults: new Dictionary<string, Result<Checks>>
            {
                [issue.PullRequestUrl] = Result<Checks>.Ok(new Checks.Green()),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ChecksVerdictResolved resolved = _dispatcher.DispatchedEvents
            .OfType<ChecksVerdictResolved>()
            .ShouldHaveSingleItem();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.RepositoryId.ShouldBe(repository.Id),
            () => resolved.IssueNumber.ShouldBe(42),
            () => resolved.Outcome.ShouldBe("passed"),
            () => resolved.FailingChecks.ShouldBeEmpty());
    }

    // --- Red verdict → "still_failing" with failing checks list ---

    [Fact]
    public async Task WhenChecksRed_RecordsChecksVerdictResolvedWithOutcomeStillFailing()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue();
        FailingCheck failingCheck = new("CI / build", "failure", "https://github.com/owner/repo/actions/runs/1");
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            checksResults: new Dictionary<string, Result<Checks>>
            {
                [issue.PullRequestUrl] = Result<Checks>.Ok(new Checks.Red([failingCheck])),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ChecksVerdictResolved resolved = _dispatcher.DispatchedEvents
            .OfType<ChecksVerdictResolved>()
            .ShouldHaveSingleItem();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.Outcome.ShouldBe("still_failing"),
            () => resolved.FailingChecks.ShouldHaveSingleItem(),
            () => resolved.FailingChecks[0].ShouldBe(failingCheck));
    }

    // --- Unfinished verdict → "unfinished" ---

    [Fact]
    public async Task WhenChecksUnfinished_RecordsChecksVerdictResolvedWithOutcomeUnfinished()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue();
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            checksResults: new Dictionary<string, Result<Checks>>
            {
                [issue.PullRequestUrl] = Result<Checks>.Ok(new Checks.Unfinished()),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ChecksVerdictResolved resolved = _dispatcher.DispatchedEvents
            .OfType<ChecksVerdictResolved>()
            .ShouldHaveSingleItem();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.Outcome.ShouldBe("unfinished"),
            () => resolved.FailingChecks.ShouldBeEmpty());
    }

    // --- Unreadable verdict → "unreadable" ---

    [Fact]
    public async Task WhenChecksUnreadable_RecordsChecksVerdictResolvedWithOutcomeUnreadable()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue();
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            checksResults: new Dictionary<string, Result<Checks>>
            {
                [issue.PullRequestUrl] = Result<Checks>.Ok(new Checks.Unreadable()),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ChecksVerdictResolved resolved = _dispatcher.DispatchedEvents
            .OfType<ChecksVerdictResolved>()
            .ShouldHaveSingleItem();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.Outcome.ShouldBe("unreadable"),
            () => resolved.FailingChecks.ShouldBeEmpty());
    }

    // --- NoneDetected within grace window → no event ---

    [Fact]
    public async Task WhenChecksNoneDetectedAndElapsedLessThanGraceWindow_RecordsNoEvent()
    {
        // Arrange — elapsed = 4 min 59 sec, grace = 5 min
        MonitoredRepository repository = SeedRepository();
        DateTimeOffset enteredAt = Now.AddMinutes(-4).AddSeconds(-59);
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue(enteredChecksPendingAt: enteredAt);
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            checksResults: new Dictionary<string, Result<Checks>>
            {
                [issue.PullRequestUrl] = Result<Checks>.Ok(new Checks.NoneDetected()),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<ChecksVerdictResolved>().ShouldBeEmpty();
    }

    // --- NoneDetected at or after grace window → "none_detected" ---

    [Fact]
    public async Task WhenChecksNoneDetectedAndElapsedEqualToGraceWindow_RecordsChecksVerdictResolvedWithOutcomeNoneDetected()
    {
        // Arrange — elapsed exactly = 5 min (grace boundary)
        MonitoredRepository repository = SeedRepository();
        DateTimeOffset enteredAt = Now.AddMinutes(-5);
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue(enteredChecksPendingAt: enteredAt);
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            checksResults: new Dictionary<string, Result<Checks>>
            {
                [issue.PullRequestUrl] = Result<Checks>.Ok(new Checks.NoneDetected()),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ChecksVerdictResolved resolved = _dispatcher.DispatchedEvents
            .OfType<ChecksVerdictResolved>()
            .ShouldHaveSingleItem();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.Outcome.ShouldBe("none_detected"),
            () => resolved.FailingChecks.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenChecksNoneDetectedAndElapsedGreaterThanGraceWindow_RecordsChecksVerdictResolvedWithOutcomeNoneDetected()
    {
        // Arrange — elapsed = 10 min, grace = 5 min
        MonitoredRepository repository = SeedRepository();
        DateTimeOffset enteredAt = Now.AddMinutes(-10);
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue(enteredChecksPendingAt: enteredAt);
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            checksResults: new Dictionary<string, Result<Checks>>
            {
                [issue.PullRequestUrl] = Result<Checks>.Ok(new Checks.NoneDetected()),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ChecksVerdictResolved resolved = _dispatcher.DispatchedEvents
            .OfType<ChecksVerdictResolved>()
            .ShouldHaveSingleItem();
        resolved.Outcome.ShouldBe("none_detected");
    }

    // --- Pending within upper bound → no event ---

    [Fact]
    public async Task WhenChecksPendingAndElapsedLessThanUpperBound_RecordsNoEvent()
    {
        // Arrange — elapsed = 59 min, upper bound = 60 min
        MonitoredRepository repository = SeedRepository();
        DateTimeOffset enteredAt = Now.AddMinutes(-59);
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue(enteredChecksPendingAt: enteredAt);
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            checksResults: new Dictionary<string, Result<Checks>>
            {
                [issue.PullRequestUrl] = Result<Checks>.Ok(new Checks.Pending()),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<ChecksVerdictResolved>().ShouldBeEmpty();
    }

    // --- Pending at or after upper bound → "unfinished" ---

    [Fact]
    public async Task WhenChecksPendingAndElapsedEqualToUpperBound_RecordsChecksVerdictResolvedWithOutcomeUnfinished()
    {
        // Arrange — elapsed exactly = 60 min (upper bound boundary)
        MonitoredRepository repository = SeedRepository();
        DateTimeOffset enteredAt = Now.AddMinutes(-60);
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue(enteredChecksPendingAt: enteredAt);
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            checksResults: new Dictionary<string, Result<Checks>>
            {
                [issue.PullRequestUrl] = Result<Checks>.Ok(new Checks.Pending()),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ChecksVerdictResolved resolved = _dispatcher.DispatchedEvents
            .OfType<ChecksVerdictResolved>()
            .ShouldHaveSingleItem();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.Outcome.ShouldBe("unfinished"),
            () => resolved.FailingChecks.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenChecksPendingAndElapsedGreaterThanUpperBound_RecordsChecksVerdictResolvedWithOutcomeUnfinished()
    {
        // Arrange — elapsed = 90 min, upper bound = 60 min
        MonitoredRepository repository = SeedRepository();
        DateTimeOffset enteredAt = Now.AddMinutes(-90);
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue(enteredChecksPendingAt: enteredAt);
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            checksResults: new Dictionary<string, Result<Checks>>
            {
                [issue.PullRequestUrl] = Result<Checks>.Ok(new Checks.Pending()),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ChecksVerdictResolved resolved = _dispatcher.DispatchedEvents
            .OfType<ChecksVerdictResolved>()
            .ShouldHaveSingleItem();
        resolved.Outcome.ShouldBe("unfinished");
    }

    // --- No checks-pending issues → no event ---

    [Fact]
    public async Task WhenNoChecksPendingIssues_RecordsNoChecksVerdictResolvedEvents()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        StubChecksPendingIssueQueries issueQueries = new([]);
        StubIssueProvider provider = new();
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<ChecksVerdictResolved>().ShouldBeEmpty();
    }

    // --- Closed/merged handling mirrors the review pass ---

    [Fact]
    public async Task WhenChecksPendingIssueIsClosedOnProvider_RaisesProviderIssueClosed()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue(issueNumber: 55);
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            isClosedResults: new Dictionary<int, Result<bool>>
            {
                [55] = Result<bool>.Ok(true),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ProviderIssueClosed closed = _dispatcher.DispatchedEvents
            .OfType<ProviderIssueClosed>()
            .ShouldHaveSingleItem();
        closed.ShouldSatisfyAllConditions(
            () => closed.RepositoryId.ShouldBe(repository.Id),
            () => closed.IssueNumber.ShouldBe(55));
        _dispatcher.DispatchedEvents.OfType<ChecksVerdictResolved>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenChecksPendingIssuePullRequestIsClosedWithoutMerge_RaisesProviderPullRequestClosed()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        string prUrl = "https://github.com/owner/repo/pull/99";
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue(issueNumber: 55, pullRequestUrl: prUrl);
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            isClosedResults: new Dictionary<int, Result<bool>>
            {
                [55] = Result<bool>.Ok(false),
            },
            pullRequestStatusResults: new Dictionary<string, Result<PullRequestStatus>>
            {
                [prUrl] = Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: true, IsMerged: false)),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ProviderPullRequestClosed closed = _dispatcher.DispatchedEvents
            .OfType<ProviderPullRequestClosed>()
            .ShouldHaveSingleItem();
        closed.ShouldSatisfyAllConditions(
            () => closed.RepositoryId.ShouldBe(repository.Id),
            () => closed.IssueNumber.ShouldBe(55));
        _dispatcher.DispatchedEvents.OfType<ChecksVerdictResolved>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenChecksPendingIssuePullRequestIsMerged_RaisesNoChecksVerdictResolved()
    {
        // Arrange — merged PR: same as review pass, records nothing for the merged case itself
        MonitoredRepository repository = SeedRepository();
        string prUrl = "https://github.com/owner/repo/pull/99";
        ChecksPendingIssueInfo issue = BuildChecksPendingIssue(issueNumber: 55, pullRequestUrl: prUrl);
        StubChecksPendingIssueQueries issueQueries = new([issue]);
        StubIssueProvider provider = new(
            isClosedResults: new Dictionary<int, Result<bool>>
            {
                [55] = Result<bool>.Ok(false),
            },
            pullRequestStatusResults: new Dictionary<string, Result<PullRequestStatus>>
            {
                [prUrl] = Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: true, IsMerged: true)),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<ChecksVerdictResolved>().ShouldBeEmpty();
        _dispatcher.DispatchedEvents.OfType<ProviderIssueClosed>().ShouldBeEmpty();
        _dispatcher.DispatchedEvents.OfType<ProviderPullRequestClosed>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGetChecksFailsForChecksPendingIssue_SkipsThatIssueAndContinues()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        string prUrl1 = "https://github.com/owner/repo/pull/10";
        string prUrl2 = "https://github.com/owner/repo/pull/20";
        ChecksPendingIssueInfo failingIssue = BuildChecksPendingIssue(issueNumber: 10, pullRequestUrl: prUrl1);
        ChecksPendingIssueInfo successIssue = BuildChecksPendingIssue(issueNumber: 20, pullRequestUrl: prUrl2);
        StubChecksPendingIssueQueries issueQueries = new([failingIssue, successIssue]);
        StubIssueProvider provider = new(
            checksResults: new Dictionary<string, Result<Checks>>
            {
                [prUrl1] = Result<Checks>.Fail(new Error("GitHub.Unavailable", "Provider error")),
                [prUrl2] = Result<Checks>.Ok(new Checks.Green()),
            });
        RepositoryPoller sut = BuildPoller(issueQueries);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ChecksVerdictResolved> events = _dispatcher.DispatchedEvents
            .OfType<ChecksVerdictResolved>()
            .ToList();
        events.Count.ShouldBe(1);
        events.ShouldContain(e => e.IssueNumber == 20);
        events.ShouldNotContain(e => e.IssueNumber == 10);
    }

    private sealed class StubChecksPendingIssueQueries(
        IReadOnlyList<ChecksPendingIssueInfo> checksPendingIssues) : IIssueQueries
    {
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

        public Task<IReadOnlyList<ChecksPendingIssueInfo>> GetChecksPendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(checksPendingIssues);

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

    private sealed class StubIssueProvider(
        IReadOnlyDictionary<string, Result<Checks>>? checksResults = null,
        IReadOnlyDictionary<int, Result<bool>>? isClosedResults = null,
        IReadOnlyDictionary<string, Result<PullRequestStatus>>? pullRequestStatusResults = null) : IIssueProvider
    {
        public Task<Result<IssueListing>> GetIssuesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IssueListing>.Ok(new IssueListing([], IsComplete: true)));

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IReadOnlyList<int>>.Ok([]));

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            if (isClosedResults is not null && isClosedResults.TryGetValue(issueNumber, out Result<bool>? result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult(Result<bool>.Ok(false));
        }

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
        {
            if (pullRequestStatusResults is not null
                && pullRequestStatusResults.TryGetValue(pullRequestUrl, out Result<PullRequestStatus>? result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult(Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)));
        }

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([], OmittedCommentCount: 0, NewestCommentAt: null)));

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<BranchProtection>.Ok(new BranchProtection("main", true, true, true)));

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit found")));

        public Task<Result<bool>> CanPushAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<Checks>> GetChecksAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
        {
            if (checksResults is not null && checksResults.TryGetValue(pullRequestUrl, out Result<Checks>? result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult(Result<Checks>.Ok(new Checks.NoneDetected()));
        }
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

    private sealed class NullDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NullEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
