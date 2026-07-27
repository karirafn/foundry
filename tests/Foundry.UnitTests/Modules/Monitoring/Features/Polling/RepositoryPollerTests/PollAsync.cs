using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
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

public sealed class PollAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventDispatcher _dispatcher;
    private readonly CapturingEligibilityEvaluator _eligibilityEvaluator;
    private readonly RepositoryPoller _sut;

    private static readonly DateTimeOffset Now = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    public PollAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _dispatcher = new CapturingIntegrationEventDispatcher();
        _eligibilityEvaluator = new CapturingEligibilityEvaluator();
        _sut = new RepositoryPoller(
            new StubIssueQueries(),
            _dbContext,
            new NullDomainEventDispatcher(),
            _dispatcher,
            _eligibilityEvaluator,
            NullLogger<RepositoryPoller>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private MonitoredRepository SeedRepository()
    {
        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, "github.com", null);
        _dbContext.Set<MonitoredRepository>().Add(repository);
        _dbContext.SaveChanges();
        return repository;
    }

    [Fact]
    public async Task WhenProviderReturnsNewIssue_RaisesIssueDetectedEvent()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ProviderIssue newIssue = new(
            Number: 1,
            Title: "Fix bug",
            Body: "Bug description",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/1",
            Labels: ["bug"],
            IssueKindLabel: "bug");
        StubIssueProvider provider = new([newIssue]);

        // Act
        Result result = await _sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IssueDetected detected = _dispatcher.DispatchedEvents
            .OfType<IssueDetected>()
            .ShouldHaveSingleItem();
        detected.ShouldSatisfyAllConditions(
            () => detected.MonitoredRepositoryId.ShouldBe(repository.Id),
            () => detected.IssueNumber.ShouldBe(1),
            () => detected.Title.ShouldBe("Fix bug"),
            () => detected.Body.ShouldBe("Bug description"),
            () => detected.Author.ShouldBe("octocat"),
            () => detected.Url.ShouldBe("https://github.com/owner/repo/issues/1"),
            () => detected.Labels.ShouldBe(["bug"]),
            () => detected.IssueKindLabel.ShouldBe("bug"));
    }

    [Fact]
    public async Task WhenKnownIssueIsUnchanged_RaisesNoEvents()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ProviderIssue existingIssue = new(
            Number: 5,
            Title: "Existing",
            Body: "Body",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/5",
            Labels: ["bug"],
            IssueKindLabel: "bug");

        IssueSnapshot snapshot = new("Existing", "Body", ["bug"]);
        StubIssueQueries issueQueries = new(
            new HashSet<int> { 5 },
            new Dictionary<int, IssueSnapshot> { [5] = snapshot });

        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([existingIssue]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<IssueDetected>().ShouldBeEmpty();
        _dispatcher.DispatchedEvents.OfType<IssueDetailsChanged>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenKnownIssueHasChangedTitle_RaisesIssueDetailsChangedEvent()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ProviderIssue updatedIssue = new(
            Number: 7,
            Title: "Updated Title",
            Body: "Same body",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/7",
            Labels: ["bug"],
            IssueKindLabel: "bug");

        IssueSnapshot oldSnapshot = new("Old Title", "Same body", ["bug"]);
        StubIssueQueries issueQueries = new(
            new HashSet<int> { 7 },
            new Dictionary<int, IssueSnapshot> { [7] = oldSnapshot });

        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([updatedIssue]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IssueDetailsChanged changed = _dispatcher.DispatchedEvents
            .OfType<IssueDetailsChanged>()
            .ShouldHaveSingleItem();
        changed.ShouldSatisfyAllConditions(
            () => changed.MonitoredRepositoryId.ShouldBe(repository.Id),
            () => changed.IssueNumber.ShouldBe(7),
            () => changed.Title.ShouldBe("Updated Title"),
            () => changed.Body.ShouldBe("Same body"),
            () => changed.Labels.ShouldBe(["bug"]));
    }

    [Fact]
    public async Task WhenKnownIssueHasLabelsSameSetDifferentOrder_RaisesNoEvents()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ProviderIssue issue = new(
            Number: 3,
            Title: "Same",
            Body: "Same",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/3",
            Labels: ["feature", "bug"],
            IssueKindLabel: "bug");

        IssueSnapshot snapshot = new("Same", "Same", ["bug", "feature"]);
        StubIssueQueries issueQueries = new(
            new HashSet<int> { 3 },
            new Dictionary<int, IssueSnapshot> { [3] = snapshot });

        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([issue]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<IssueDetected>().ShouldBeEmpty();
        _dispatcher.DispatchedEvents.OfType<IssueDetailsChanged>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenKnownIssueHasDifferentLabelSet_RaisesIssueDetailsChangedEvent()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ProviderIssue issue = new(
            Number: 9,
            Title: "Same",
            Body: "Same",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/9",
            Labels: ["bug", "priority"],
            IssueKindLabel: "bug");

        IssueSnapshot snapshot = new("Same", "Same", ["bug"]);
        StubIssueQueries issueQueries = new(
            new HashSet<int> { 9 },
            new Dictionary<int, IssueSnapshot> { [9] = snapshot });

        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([issue]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents
            .OfType<IssueDetailsChanged>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenProviderFails_ReturnsFailureAndRaisesNoEvents()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        Error providerError = new("GitHub.Unavailable", "Service unavailable");
        FailingIssueProvider provider = new(providerError);

        // Act
        Result result = await _sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.ShouldBe(providerError);
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenProviderReturnsMultipleNewIssues_RaisesMultipleIssueDetectedEvents()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ProviderIssue issue1 = new(
            Number: 10,
            Title: "Issue Ten",
            Body: "Body",
            Author: "user",
            Url: "https://github.com/owner/repo/issues/10",
            Labels: [],
            IssueKindLabel: "feature");
        ProviderIssue issue2 = new(
            Number: 11,
            Title: "Issue Eleven",
            Body: "Body",
            Author: "user",
            Url: "https://github.com/owner/repo/issues/11",
            Labels: [],
            IssueKindLabel: "feature");
        StubIssueProvider provider = new([issue1, issue2]);

        // Act
        Result result = await _sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<IssueDetected> detected = _dispatcher.DispatchedEvents
            .OfType<IssueDetected>()
            .ToList();
        detected.Count.ShouldBe(2);
        detected.ShouldContain(e => e.IssueNumber == 10);
        detected.ShouldContain(e => e.IssueNumber == 11);
    }

    [Fact]
    public async Task WhenKnownIssueExists_RaisesIssueDependenciesDetectedEvent()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        StubIssueQueries issueQueries = new(
            new HashSet<int> { 42 },
            new Dictionary<int, IssueSnapshot>());
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IssueDependenciesDetected detected = _dispatcher.DispatchedEvents
            .OfType<IssueDependenciesDetected>()
            .ShouldHaveSingleItem();
        detected.ShouldSatisfyAllConditions(
            () => detected.MonitoredRepositoryId.ShouldBe(repository.Id),
            () => detected.IssueNumber.ShouldBe(42),
            () => detected.BlockedByIssueNumbers.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenProviderReturnsDependencies_EventCarriesBlockedByIssueNumbers()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        Dictionary<int, Result<IReadOnlyList<int>>> dependencyResults = new()
        {
            [42] = Result<IReadOnlyList<int>>.Ok([10, 20]),
        };
        StubIssueQueries issueQueries = new(
            new HashSet<int> { 42 },
            new Dictionary<int, IssueSnapshot>());
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([], dependencyResults);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IssueDependenciesDetected detected = _dispatcher.DispatchedEvents
            .OfType<IssueDependenciesDetected>()
            .ShouldHaveSingleItem();
        detected.BlockedByIssueNumbers.ShouldBe([10, 20]);
    }

    [Fact]
    public async Task WhenProviderReturnsEmptyDependencies_EventRaisedWithEmptyBlockedByList()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        Dictionary<int, Result<IReadOnlyList<int>>> dependencyResults = new()
        {
            [7] = Result<IReadOnlyList<int>>.Ok([]),
        };
        StubIssueQueries issueQueries = new(
            new HashSet<int> { 7 },
            new Dictionary<int, IssueSnapshot>());
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([], dependencyResults);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IssueDependenciesDetected detected = _dispatcher.DispatchedEvents
            .OfType<IssueDependenciesDetected>()
            .ShouldHaveSingleItem();
        detected.BlockedByIssueNumbers.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGetDependenciesFailsForOneIssue_SkipsThatIssueAndRaisesEventsForOthers()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        Error dependencyError = new("GitHub.RateLimited", "Rate limited");
        Dictionary<int, Result<IReadOnlyList<int>>> dependencyResults = new()
        {
            [5] = Result<IReadOnlyList<int>>.Fail(dependencyError),
            [6] = Result<IReadOnlyList<int>>.Ok([99]),
        };
        StubIssueQueries issueQueries = new(
            new HashSet<int> { 5, 6 },
            new Dictionary<int, IssueSnapshot>());
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([], dependencyResults);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<IssueDependenciesDetected> detectedEvents = _dispatcher.DispatchedEvents
            .OfType<IssueDependenciesDetected>()
            .ToList();
        detectedEvents.Count.ShouldBe(1);
        detectedEvents.ShouldContain(e => e.IssueNumber == 6);
        detectedEvents.ShouldNotContain(e => e.IssueNumber == 5);
    }

    [Fact]
    public async Task WhenNewIssueDetectedInSameCycle_AlsoGetsDependencyCheck()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ProviderIssue newIssue = new(
            Number: 15,
            Title: "New issue",
            Body: "Body",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/15",
            Labels: [],
            IssueKindLabel: "feature");

        // Pass 1: issue 15 is new (not in initial known numbers).
        // After dispatch, issue 15 is now persisted — second call returns {15, 16}.
        StubIssueQueries issueQueries = new(
            knownNumbers: new HashSet<int> { 16 },
            snapshots: new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: new HashSet<int> { 15, 16 });

        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([newIssue]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<IssueDependenciesDetected> dependencyEvents = _dispatcher.DispatchedEvents
            .OfType<IssueDependenciesDetected>()
            .ToList();
        dependencyEvents.Count.ShouldBe(2);
        dependencyEvents.ShouldContain(e => e.IssueNumber == 15);
        dependencyEvents.ShouldContain(e => e.IssueNumber == 16);
    }

    [Fact]
    public async Task WhenReviewIssueIsClosedOnProvider_RaisesProviderIssueClosed()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ReviewIssueInfo reviewIssue = new(
            IssueNumber: 42,
            PullRequestUrl: "https://github.com/owner/repo/pull/99",
            FeedbackCutoffAt: Now);
        StubIssueQueries issueQueries = new(
            new HashSet<int>(),
            new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: [reviewIssue]);

        Dictionary<int, Result<bool>> isClosedResults = new()
        {
            [42] = Result<bool>.Ok(true),
        };
        StubIssueProvider provider = new([], isClosedResults: isClosedResults);
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ProviderIssueClosed closed = _dispatcher.DispatchedEvents
            .OfType<ProviderIssueClosed>()
            .ShouldHaveSingleItem();
        closed.ShouldSatisfyAllConditions(
            () => closed.RepositoryId.ShouldBe(repository.Id),
            () => closed.IssueNumber.ShouldBe(42));
    }

    [Fact]
    public async Task WhenReviewIssuePullRequestIsClosedWithoutMerge_RaisesProviderPullRequestClosed()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        string prUrl = "https://github.com/owner/repo/pull/99";
        ReviewIssueInfo reviewIssue = new(IssueNumber: 42, PullRequestUrl: prUrl, FeedbackCutoffAt: Now);
        StubIssueQueries issueQueries = new(
            new HashSet<int>(),
            new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: [reviewIssue]);

        Dictionary<int, Result<bool>> isClosedResults = new()
        {
            [42] = Result<bool>.Ok(false),
        };
        Dictionary<string, Result<PullRequestStatus>> prStatusResults = new()
        {
            [prUrl] = Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: true, IsMerged: false)),
        };
        StubIssueProvider provider = new([], isClosedResults: isClosedResults, pullRequestStatusResults: prStatusResults);
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ProviderPullRequestClosed closed = _dispatcher.DispatchedEvents
            .OfType<ProviderPullRequestClosed>()
            .ShouldHaveSingleItem();
        closed.ShouldSatisfyAllConditions(
            () => closed.RepositoryId.ShouldBe(repository.Id),
            () => closed.IssueNumber.ShouldBe(42));
    }

    [Fact]
    public async Task WhenReviewIssueIsOpenAndPullRequestIsOpen_RaisesNoReviewStatusEvents()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        string prUrl = "https://github.com/owner/repo/pull/99";
        ReviewIssueInfo reviewIssue = new(IssueNumber: 42, PullRequestUrl: prUrl, FeedbackCutoffAt: Now);
        StubIssueQueries issueQueries = new(
            new HashSet<int>(),
            new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: [reviewIssue]);

        Dictionary<int, Result<bool>> isClosedResults = new()
        {
            [42] = Result<bool>.Ok(false),
        };
        Dictionary<string, Result<PullRequestStatus>> prStatusResults = new()
        {
            [prUrl] = Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)),
        };
        StubIssueProvider provider = new([], isClosedResults: isClosedResults, pullRequestStatusResults: prStatusResults);
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<ProviderIssueClosed>().ShouldBeEmpty();
        _dispatcher.DispatchedEvents.OfType<ProviderPullRequestClosed>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIsIssueClosedFailsForOneReviewIssue_SkipsThatIssueAndContinues()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        string prUrl = "https://github.com/owner/repo/pull/200";
        ReviewIssueInfo failingIssue = new(
            IssueNumber: 10,
            PullRequestUrl: "https://github.com/owner/repo/pull/100",
            FeedbackCutoffAt: Now);
        ReviewIssueInfo successIssue = new(IssueNumber: 20, PullRequestUrl: prUrl, FeedbackCutoffAt: Now);
        StubIssueQueries issueQueries = new(
            new HashSet<int>(),
            new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: [failingIssue, successIssue]);

        Error providerError = new("GitHub.RateLimited", "Rate limited");
        Dictionary<int, Result<bool>> isClosedResults = new()
        {
            [10] = Result<bool>.Fail(providerError),
            [20] = Result<bool>.Ok(true),
        };
        StubIssueProvider provider = new([], isClosedResults: isClosedResults);
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderIssueClosed> closedEvents = _dispatcher.DispatchedEvents
            .OfType<ProviderIssueClosed>()
            .ToList();
        closedEvents.Count.ShouldBe(1);
        closedEvents.ShouldContain(e => e.IssueNumber == 20);
        closedEvents.ShouldNotContain(e => e.IssueNumber == 10);
    }

    [Fact]
    public async Task WhenReviewIssuePullRequestIsClosedAndMerged_RaisesNoEvents()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        string prUrl = "https://github.com/owner/repo/pull/99";
        ReviewIssueInfo reviewIssue = new(IssueNumber: 42, PullRequestUrl: prUrl, FeedbackCutoffAt: Now);
        StubIssueQueries issueQueries = new(
            new HashSet<int>(),
            new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: [reviewIssue]);

        Dictionary<int, Result<bool>> isClosedResults = new()
        {
            [42] = Result<bool>.Ok(false),
        };
        Dictionary<string, Result<PullRequestStatus>> prStatusResults = new()
        {
            [prUrl] = Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: true, IsMerged: true)),
        };
        StubIssueProvider provider = new([], isClosedResults: isClosedResults, pullRequestStatusResults: prStatusResults);
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<ProviderIssueClosed>().ShouldBeEmpty();
        _dispatcher.DispatchedEvents.OfType<ProviderPullRequestClosed>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGetPullRequestStatusFailsForReviewIssue_SkipsThatIssue()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        string prUrl = "https://github.com/owner/repo/pull/99";
        ReviewIssueInfo reviewIssue = new(IssueNumber: 42, PullRequestUrl: prUrl, FeedbackCutoffAt: Now);
        StubIssueQueries issueQueries = new(
            new HashSet<int>(),
            new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: [reviewIssue]);

        Dictionary<int, Result<bool>> isClosedResults = new()
        {
            [42] = Result<bool>.Ok(false),
        };
        Error providerError = new("GitHub.Unavailable", "Service unavailable");
        Dictionary<string, Result<PullRequestStatus>> prStatusResults = new()
        {
            [prUrl] = Result<PullRequestStatus>.Fail(providerError),
        };
        StubIssueProvider provider = new([], isClosedResults: isClosedResults, pullRequestStatusResults: prStatusResults);
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<ProviderPullRequestClosed>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGetReviewIssuesReturnsEmpty_RaisesNoReviewStatusEvents()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        // StubIssueQueries defaults to empty review issues; IsIssueClosedAsync never called.
        StubIssueProvider provider = new([]);

        // Act
        Result result = await _sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<ProviderIssueClosed>().ShouldBeEmpty();
        _dispatcher.DispatchedEvents.OfType<ProviderPullRequestClosed>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenProviderReturnsNoIssues_RaisesNoEventsAndUpdatesLastPolledAt()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        StubIssueProvider provider = new([]);

        // Act
        Result result = await _sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        repository.LastPolledAt.ShouldBe(Now);
    }

    [Fact]
    public async Task WhenReviewIssuePrIsOpenAndProviderReturnsFeedback_RaisesPullRequestChangesRequested()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        string prUrl = "https://github.com/owner/repo/pull/99";
        DateTimeOffset cutoff = Now.AddHours(-1);
        ReviewIssueInfo reviewIssue = new(IssueNumber: 42, PullRequestUrl: prUrl, FeedbackCutoffAt: cutoff);
        StubIssueQueries issueQueries = new(
            new HashSet<int>(),
            new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: [reviewIssue]);

        Dictionary<int, Result<bool>> isClosedResults = new()
        {
            [42] = Result<bool>.Ok(false),
        };
        Dictionary<string, Result<PullRequestStatus>> prStatusResults = new()
        {
            [prUrl] = Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)),
        };
        ReviewComment comment = new("Please fix the indentation", "src/Foo.cs", 42);
        Dictionary<string, Result<ReviewFeedback>> feedbackResults = new()
        {
            [prUrl] = Result<ReviewFeedback>.Ok(new ReviewFeedback([comment])),
        };
        StubIssueProvider provider = new(
            [],
            isClosedResults: isClosedResults,
            pullRequestStatusResults: prStatusResults,
            reviewFeedbackResults: feedbackResults);
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        PullRequestChangesRequested changesRequested = _dispatcher.DispatchedEvents
            .OfType<PullRequestChangesRequested>()
            .ShouldHaveSingleItem();
        changesRequested.ShouldSatisfyAllConditions(
            () => changesRequested.RepositoryId.ShouldBe(repository.Id),
            () => changesRequested.IssueNumber.ShouldBe(42),
            () => changesRequested.Comments.Count.ShouldBe(1),
            () => changesRequested.Comments[0].ShouldBe(comment));
    }

    [Fact]
    public async Task WhenReviewIssuePrIsOpenAndProviderReturnsEmptyFeedback_RaisesNoPullRequestChangesRequested()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        string prUrl = "https://github.com/owner/repo/pull/99";
        ReviewIssueInfo reviewIssue = new(IssueNumber: 42, PullRequestUrl: prUrl, FeedbackCutoffAt: Now);
        StubIssueQueries issueQueries = new(
            new HashSet<int>(),
            new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: [reviewIssue]);

        Dictionary<int, Result<bool>> isClosedResults = new()
        {
            [42] = Result<bool>.Ok(false),
        };
        Dictionary<string, Result<PullRequestStatus>> prStatusResults = new()
        {
            [prUrl] = Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)),
        };
        Dictionary<string, Result<ReviewFeedback>> feedbackResults = new()
        {
            [prUrl] = Result<ReviewFeedback>.Ok(new ReviewFeedback([])),
        };
        StubIssueProvider provider = new(
            [],
            isClosedResults: isClosedResults,
            pullRequestStatusResults: prStatusResults,
            reviewFeedbackResults: feedbackResults);
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<PullRequestChangesRequested>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGetReviewFeedbackFailsForReviewIssue_SkipsThatIssueAndContinues()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        string prUrl1 = "https://github.com/owner/repo/pull/10";
        string prUrl2 = "https://github.com/owner/repo/pull/20";
        ReviewIssueInfo failingIssue = new(IssueNumber: 10, PullRequestUrl: prUrl1, FeedbackCutoffAt: Now);
        ReviewIssueInfo successIssue = new(IssueNumber: 20, PullRequestUrl: prUrl2, FeedbackCutoffAt: Now);
        StubIssueQueries issueQueries = new(
            new HashSet<int>(),
            new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: [failingIssue, successIssue]);

        Dictionary<int, Result<bool>> isClosedResults = new()
        {
            [10] = Result<bool>.Ok(false),
            [20] = Result<bool>.Ok(false),
        };
        Dictionary<string, Result<PullRequestStatus>> prStatusResults = new()
        {
            [prUrl1] = Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)),
            [prUrl2] = Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)),
        };
        Error feedbackError = new("GitHub.RateLimited", "Rate limited");
        ReviewComment comment = new("Please address this");
        Dictionary<string, Result<ReviewFeedback>> feedbackResults = new()
        {
            [prUrl1] = Result<ReviewFeedback>.Fail(feedbackError),
            [prUrl2] = Result<ReviewFeedback>.Ok(new ReviewFeedback([comment])),
        };
        StubIssueProvider provider = new(
            [],
            isClosedResults: isClosedResults,
            pullRequestStatusResults: prStatusResults,
            reviewFeedbackResults: feedbackResults);
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<PullRequestChangesRequested> events = _dispatcher.DispatchedEvents
            .OfType<PullRequestChangesRequested>()
            .ToList();
        events.Count.ShouldBe(1);
        events.ShouldContain(e => e.IssueNumber == 20);
        events.ShouldNotContain(e => e.IssueNumber == 10);
    }

    [Fact]
    public async Task WhenReviewIssuePrIsClosedAndMerged_DoesNotCallGetReviewFeedback()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        string prUrl = "https://github.com/owner/repo/pull/99";
        ReviewIssueInfo reviewIssue = new(IssueNumber: 42, PullRequestUrl: prUrl, FeedbackCutoffAt: Now);
        StubIssueQueries issueQueries = new(
            new HashSet<int>(),
            new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: [reviewIssue]);

        Dictionary<int, Result<bool>> isClosedResults = new()
        {
            [42] = Result<bool>.Ok(false),
        };
        Dictionary<string, Result<PullRequestStatus>> prStatusResults = new()
        {
            [prUrl] = Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: true, IsMerged: true)),
        };
        // No feedback configured — if called, stub returns empty feedback (not a problem for this test's assertion)
        StubIssueProvider provider = new([], isClosedResults: isClosedResults, pullRequestStatusResults: prStatusResults);
        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<PullRequestChangesRequested>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRepoHasNoTrackedIssues_EligibilityIsStillEvaluated()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        StubIssueProvider provider = new([]);

        // Act
        Result result = await _sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _eligibilityEvaluator.EvaluateCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenPollRunsTwice_EligibilityIsEvaluatedOnEachCycle()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        StubIssueProvider provider = new([]);

        // Act
        await _sut.PollAsync(repository, provider, Now, CancellationToken.None);
        await _sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        _eligibilityEvaluator.EvaluateCallCount.ShouldBe(2);
    }

    [Fact]
    public async Task WhenEvaluatorSetsEligibleOnRepo_RepoEligibilityReflectsEligible()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        CapturingEligibilityEvaluator evaluator = new(new RepositoryEligibility.Eligible());
        RepositoryPoller sut = new(
            new StubIssueQueries(),
            _dbContext,
            new NullDomainEventDispatcher(),
            _dispatcher,
            evaluator,
            NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        repository.Eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>();
    }

    [Fact]
    public async Task WhenEvaluatorSetsIneligibleOnRepo_RepoEligibilityReflectsIneligible()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.AllowDirectPushes()]);
        CapturingEligibilityEvaluator evaluator = new(ineligible);
        RepositoryPoller sut = new(
            new StubIssueQueries(),
            _dbContext,
            new NullDomainEventDispatcher(),
            _dispatcher,
            evaluator,
            NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        RepositoryEligibility.Ineligible eligibility = repository.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        eligibility.Violations.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenRestingStateIssueAbsentFromNonEmptyFetch_RaisesProviderIssueUntrackedEvent()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();

        // Issue 7 is a resting-state issue that no longer appears in the provider fetch.
        // The fetch is non-empty (issue 8 is still present) so the empty-fetch guard does not trigger.
        ProviderIssue otherIssue = new(
            Number: 8,
            Title: "Other",
            Body: "Body",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/8",
            Labels: ["foundry"],
            IssueKindLabel: "feature");
        StubIssueQueries issueQueries = new(
            knownNumbers: new HashSet<int> { 7, 8 },
            snapshots: new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: null,
            untrackableNumbers: new HashSet<int> { 7 });

        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([otherIssue]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ProviderIssueUntracked untracked = _dispatcher.DispatchedEvents
            .OfType<ProviderIssueUntracked>()
            .ShouldHaveSingleItem();
        untracked.ShouldSatisfyAllConditions(
            () => untracked.RepositoryId.ShouldBe(repository.Id),
            () => untracked.IssueNumber.ShouldBe(7));
    }

    [Fact]
    public async Task WhenRestingStateIssueStillPresentInFetch_RaisesNoUntrackedEvent()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ProviderIssue existingIssue = new(
            Number: 7,
            Title: "Existing",
            Body: "Body",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/7",
            Labels: ["bug"],
            IssueKindLabel: "bug");
        IssueSnapshot snapshot = new("Existing", "Body", ["bug"]);
        StubIssueQueries issueQueries = new(
            knownNumbers: new HashSet<int> { 7 },
            snapshots: new Dictionary<int, IssueSnapshot> { [7] = snapshot },
            knownNumbersSecondCall: null,
            reviewIssues: null,
            untrackableNumbers: new HashSet<int> { 7 });

        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([existingIssue]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<ProviderIssueUntracked>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenPreservedStateIssueAbsentFromFetch_RaisesNoUntrackedEvent()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();

        // Issue 42 is absent from fetch but NOT in untrackable numbers (e.g. it is completed/in-progress)
        StubIssueQueries issueQueries = new(
            knownNumbers: new HashSet<int> { 42 },
            snapshots: new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: null,
            untrackableNumbers: new HashSet<int>());

        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<ProviderIssueUntracked>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenMultipleRestingStateIssuesAbsentFromNonEmptyFetch_RaisesUntrackedEventForEach()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();

        // Issues 5 and 6 are absent; issue 99 is still present so the empty-fetch guard does not trigger.
        ProviderIssue otherIssue = new(
            Number: 99,
            Title: "Still here",
            Body: "Body",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/99",
            Labels: ["foundry"],
            IssueKindLabel: "feature");
        StubIssueQueries issueQueries = new(
            knownNumbers: new HashSet<int> { 5, 6, 99 },
            snapshots: new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: null,
            untrackableNumbers: new HashSet<int> { 5, 6 });

        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([otherIssue]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderIssueUntracked> untrackedEvents = _dispatcher.DispatchedEvents
            .OfType<ProviderIssueUntracked>()
            .ToList();
        untrackedEvents.Count.ShouldBe(2);
        untrackedEvents.ShouldContain(e => e.IssueNumber == 5);
        untrackedEvents.ShouldContain(e => e.IssueNumber == 6);
    }

    [Fact]
    public async Task WhenFetchReturnsEmptyListButRestingIssuesKnown_SkipsUntrackPassAndRaisesNoUntrackedEvents()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();

        // Two resting-state issues are known; the provider returns an empty list (transient upstream blip).
        StubIssueQueries issueQueries = new(
            knownNumbers: new HashSet<int> { 3, 4 },
            snapshots: new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: null,
            reviewIssues: null,
            untrackableNumbers: new HashSet<int> { 3, 4 });

        RepositoryPoller sut = new(issueQueries, _dbContext, new NullDomainEventDispatcher(), _dispatcher, _eligibilityEvaluator, NullLogger<RepositoryPoller>.Instance);
        StubIssueProvider provider = new([]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _dispatcher.DispatchedEvents.OfType<ProviderIssueUntracked>().ShouldBeEmpty();
    }

    private sealed class StubIssueProvider(
        IReadOnlyList<ProviderIssue> issues,
        IReadOnlyDictionary<int, Result<IReadOnlyList<int>>>? dependencyResults = null,
        IReadOnlyDictionary<int, Result<bool>>? isClosedResults = null,
        IReadOnlyDictionary<string, Result<PullRequestStatus>>? pullRequestStatusResults = null,
        IReadOnlyDictionary<string, Result<ReviewFeedback>>? reviewFeedbackResults = null,
        Result<BranchProtection>? branchProtectionResult = null) : IIssueProvider
    {
        public StubIssueProvider() : this([])
        {
        }

        public Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<IReadOnlyList<ProviderIssue>>.Ok(issues));
        }

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            if (dependencyResults is not null && dependencyResults.TryGetValue(issueNumber, out Result<IReadOnlyList<int>>? result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult(Result<IReadOnlyList<int>>.Ok([]));
        }

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
        {
            if (reviewFeedbackResults is not null
                && reviewFeedbackResults.TryGetValue(pullRequestUrl, out Result<ReviewFeedback>? result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([])));
        }

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                branchProtectionResult
                ?? Result<BranchProtection>.Ok(new BranchProtection("main", true, true, true)));
        }

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<bool>.Ok(true));
        }

        public Task<Result<bool>> HasBranchCommitsAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<bool>.Ok(false));
        }

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));
        }

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));
        }

        public Task<Result<bool>> CanPushAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));
    }

    private sealed class FailingIssueProvider(Error error) : IIssueProvider
    {
        public Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<IReadOnlyList<ProviderIssue>>.Fail(error));
        }

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<IReadOnlyList<int>>.Fail(error));
        }

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<bool>.Fail(error));
        }

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<PullRequestStatus>.Fail(error));
        }

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<ReviewFeedback>.Fail(error));
        }

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<BranchProtection>.Fail(error));
        }

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<bool>.Fail(error));
        }

        public Task<Result<bool>> HasBranchCommitsAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<bool>.Fail(error));
        }

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<MergeRequestByBranch>.Fail(error));
        }

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<LatestBranchCommit>.Fail(error));
        }

        public Task<Result<bool>> CanPushAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));
    }

    private sealed class StubIssueQueries : IIssueQueries
    {
        private readonly IReadOnlySet<int> _knownNumbers;
        private readonly IReadOnlyDictionary<int, IssueSnapshot> _snapshots;
        private readonly IReadOnlySet<int>? _knownNumbersSecondCall;
        private readonly IReadOnlyList<ReviewIssueInfo> _reviewIssues;
        private readonly IReadOnlySet<int> _untrackableNumbers;
        private int _getKnownNumbersCallCount;

        public StubIssueQueries()
            : this(new HashSet<int>(), new Dictionary<int, IssueSnapshot>())
        {
        }

        public StubIssueQueries(IReadOnlySet<int> knownNumbers, IReadOnlyDictionary<int, IssueSnapshot> snapshots)
            : this(knownNumbers, snapshots, knownNumbersSecondCall: null)
        {
        }

        public StubIssueQueries(
            IReadOnlySet<int> knownNumbers,
            IReadOnlyDictionary<int, IssueSnapshot> snapshots,
            IReadOnlySet<int>? knownNumbersSecondCall,
            IReadOnlyList<ReviewIssueInfo>? reviewIssues = null,
            IReadOnlySet<int>? untrackableNumbers = null)
        {
            _knownNumbers = knownNumbers;
            _snapshots = snapshots;
            _knownNumbersSecondCall = knownNumbersSecondCall;
            _reviewIssues = reviewIssues ?? [];
            _untrackableNumbers = untrackableNumbers ?? new HashSet<int>();
        }

        public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            _getKnownNumbersCallCount++;
            IReadOnlySet<int> numbers = _knownNumbersSecondCall is not null && _getKnownNumbersCallCount >= 2
                ? _knownNumbersSecondCall
                : _knownNumbers;
            return Task.FromResult(numbers);
        }

        public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
            MonitoredRepositoryId repositoryId,
            IReadOnlySet<int> issueNumbers,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_snapshots);
        }

        public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DependencyEdge>>([]);
        }

        public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_reviewIssues);
        }

        public Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_untrackableNumbers);
        }

        public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<IssueSummary>>([]);
        }

        public Task<IssueSummary?> GetIssueSummaryAsync(IssueId issueId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IssueSummary?>(null);
        }

        public Task<Result<IssueDetail>> GetIssueDetailAsync(IssueId issueId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<IssueDetail>.Fail(IssueErrors.NotFound(issueId)));
        }

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
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingEligibilityEvaluator(RepositoryEligibility? eligibility = null)
        : IRepositoryEligibilityEvaluator
    {
        private readonly RepositoryEligibility _eligibility = eligibility ?? new RepositoryEligibility.Eligible();

        public int EvaluateCallCount { get; private set; }

        public Task EvaluateAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
        {
            EvaluateCallCount++;
            repo.SetEligibility(_eligibility);
            return Task.CompletedTask;
        }
    }
}
