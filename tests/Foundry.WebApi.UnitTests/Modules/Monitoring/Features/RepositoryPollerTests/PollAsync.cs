using Foundry.WebApi.Modules.Issues;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Modules.Monitoring.Features;
using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Features.RepositoryPollerTests;

public sealed class PollAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly RepositoryPoller _sut;

    private static readonly DateTimeOffset Now = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("owner/repo")).Value;

    public PollAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _dispatcher = new CapturingDomainEventDispatcher();
        _sut = new RepositoryPoller(new StubIssuesModule(), _dbContext, _dispatcher);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private MonitoredRepository SeedRepository()
    {
        GitHubAccount account = GitHubAccount.Create("my-org", "TOKEN", new Uri("https://api.github.com"));
        _dbContext.Set<Account>().Add(account);
        _dbContext.SaveChanges();

        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, account.Id, null);
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
            Labels: ["bug"]);
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
            () => detected.Labels.ShouldBe(["bug"]));
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
            Labels: ["bug"]);

        IssueSnapshot snapshot = new("Existing", "Body", ["bug"]);
        StubIssuesModule issuesModule = new(
            new HashSet<int> { 5 },
            new Dictionary<int, IssueSnapshot> { [5] = snapshot });

        RepositoryPoller sut = new(issuesModule, _dbContext, _dispatcher);
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
            Labels: ["bug"]);

        IssueSnapshot oldSnapshot = new("Old Title", "Same body", ["bug"]);
        StubIssuesModule issuesModule = new(
            new HashSet<int> { 7 },
            new Dictionary<int, IssueSnapshot> { [7] = oldSnapshot });

        RepositoryPoller sut = new(issuesModule, _dbContext, _dispatcher);
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
            Labels: ["feature", "bug"]);

        IssueSnapshot snapshot = new("Same", "Same", ["bug", "feature"]);
        StubIssuesModule issuesModule = new(
            new HashSet<int> { 3 },
            new Dictionary<int, IssueSnapshot> { [3] = snapshot });

        RepositoryPoller sut = new(issuesModule, _dbContext, _dispatcher);
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
            Labels: ["bug", "priority"]);

        IssueSnapshot snapshot = new("Same", "Same", ["bug"]);
        StubIssuesModule issuesModule = new(
            new HashSet<int> { 9 },
            new Dictionary<int, IssueSnapshot> { [9] = snapshot });

        RepositoryPoller sut = new(issuesModule, _dbContext, _dispatcher);
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
            Labels: []);
        ProviderIssue issue2 = new(
            Number: 11,
            Title: "Issue Eleven",
            Body: "Body",
            Author: "user",
            Url: "https://github.com/owner/repo/issues/11",
            Labels: []);
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
        StubIssuesModule issuesModule = new(
            new HashSet<int> { 42 },
            new Dictionary<int, IssueSnapshot>());
        RepositoryPoller sut = new(issuesModule, _dbContext, _dispatcher);
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
        StubIssuesModule issuesModule = new(
            new HashSet<int> { 42 },
            new Dictionary<int, IssueSnapshot>());
        RepositoryPoller sut = new(issuesModule, _dbContext, _dispatcher);
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
        StubIssuesModule issuesModule = new(
            new HashSet<int> { 7 },
            new Dictionary<int, IssueSnapshot>());
        RepositoryPoller sut = new(issuesModule, _dbContext, _dispatcher);
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
        StubIssuesModule issuesModule = new(
            new HashSet<int> { 5, 6 },
            new Dictionary<int, IssueSnapshot>());
        RepositoryPoller sut = new(issuesModule, _dbContext, _dispatcher);
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
            Labels: []);

        // Pass 1: issue 15 is new (not in initial known numbers).
        // After dispatch, issue 15 is now persisted — second call returns {15, 16}.
        StubIssuesModule issuesModule = new(
            knownNumbers: new HashSet<int> { 16 },
            snapshots: new Dictionary<int, IssueSnapshot>(),
            knownNumbersSecondCall: new HashSet<int> { 15, 16 });

        RepositoryPoller sut = new(issuesModule, _dbContext, _dispatcher);
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

    private sealed class StubIssueProvider(
        IReadOnlyList<ProviderIssue> issues,
        IReadOnlyDictionary<int, Result<IReadOnlyList<int>>>? dependencyResults = null) : IIssueProvider
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
    }

    private sealed class StubIssuesModule : IIssuesModule
    {
        private readonly IReadOnlySet<int> _knownNumbers;
        private readonly IReadOnlyDictionary<int, IssueSnapshot> _snapshots;
        private readonly IReadOnlySet<int>? _knownNumbersSecondCall;
        private int _getKnownNumbersCallCount;

        public StubIssuesModule()
            : this(new HashSet<int>(), new Dictionary<int, IssueSnapshot>())
        {
        }

        public StubIssuesModule(IReadOnlySet<int> knownNumbers, IReadOnlyDictionary<int, IssueSnapshot> snapshots)
            : this(knownNumbers, snapshots, knownNumbersSecondCall: null)
        {
        }

        public StubIssuesModule(
            IReadOnlySet<int> knownNumbers,
            IReadOnlyDictionary<int, IssueSnapshot> snapshots,
            IReadOnlySet<int>? knownNumbersSecondCall)
        {
            _knownNumbers = knownNumbers;
            _snapshots = snapshots;
            _knownNumbersSecondCall = knownNumbersSecondCall;
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

        public Task<ClaimedIssueDispatch?> ClaimNextQueuedIssueAsync(Guid workerRunId, CancellationToken cancellationToken)
        {
            return Task.FromResult<ClaimedIssueDispatch?>(null);
        }
    }

    private sealed class CapturingDomainEventDispatcher : IDomainEventDispatcher
    {
        private readonly List<IDomainEvent> _events = [];

        public IReadOnlyList<IDomainEvent> DispatchedEvents => _events;

        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
        {
            _events.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
