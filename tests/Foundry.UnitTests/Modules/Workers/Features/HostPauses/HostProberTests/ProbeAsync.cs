using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.HostPauses;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.HostPauses.HostProberTests;

public sealed class ProbeAsync : IAsyncDisposable
{
    private const string Host = "gitlab.example.test";

    private static readonly DateTimeOffset PausedAt = new(2026, 11, 13, 7, 39, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = PausedAt.AddMinutes(1);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly FakePublisher _publisher = new();
    private readonly CapturingSystemNotificationBroadcaster _broadcaster = new();
    private readonly Dictionary<MonitoredRepositoryId, RepositoryDispatchInfo?> _dispatchInfo = [];
    private readonly List<MonitoredRepositoryId> _repositoriesOnHost = [];

    public ProbeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _dbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();

        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseHost(Host, PausedAt);
        _dbContext.Set<GlobalSettings>().Add(settings);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenTheFirstRepositoryIsReachable_ClearsThePauseAndBroadcasts()
    {
        // Arrange
        AddRepository("owner/a");

        // Act
        await BuildProber().ProbeAsync(Host, TestContext.Current.CancellationToken);

        // Assert
        (await ReadHostPausesAsync()).ShouldBeEmpty();
        _publisher.ProbeRequests.ShouldHaveSingleItem();
        SentNotificationShouldBeDispatch(isActive: false);
    }

    [Fact]
    public async Task WhenAProbeIsReachable_StopsProbingFurtherRepositories()
    {
        // Arrange
        AddRepository("owner/a");
        AddRepository("owner/b");
        AddRepository("owner/c");
        _publisher.WithProbeOutcomes(new HostProbeOutcome.Unreachable("dns"), new HostProbeOutcome.Reachable());

        // Act
        await BuildProber().ProbeAsync(Host, TestContext.Current.CancellationToken);

        // Assert
        (await ReadHostPausesAsync()).ShouldBeEmpty();
        _publisher.ProbeRequests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenEveryProbedRepositoryIsUnreachable_ProbesAtMostThreeAndAdvancesTheBackoff()
    {
        // Arrange
        foreach (string slug in new[] { "owner/a", "owner/b", "owner/c", "owner/d" })
        {
            AddRepository(slug);
        }

        _publisher.WithProbeOutcomes(
            new HostProbeOutcome.Unreachable("dns"),
            new HostProbeOutcome.Unreachable("dns"),
            new HostProbeOutcome.Unreachable("dns"),
            new HostProbeOutcome.Reachable());

        // Act
        await BuildProber().ProbeAsync(Host, TestContext.Current.CancellationToken);

        // Assert
        _publisher.ProbeRequests.Count.ShouldBe(HostProber.MaxRepositoriesPerProbe);
        HostPause pause = (await ReadHostPausesAsync()).ShouldHaveSingleItem();
        pause.ShouldBe(HostPause.Start(Host, PausedAt).AfterUnreachableProbe(Now));
        SentNotificationShouldBeDispatch(isActive: true);
    }

    [Fact]
    public async Task WhenAProbeIsInconclusive_RecordsTheProbeAsUnreachable()
    {
        // Arrange
        AddRepository("owner/a");
        _publisher.WithProbeOutcomes(new HostProbeOutcome.Inconclusive("no FOUNDRY_PROBE line"));

        // Act
        await BuildProber().ProbeAsync(Host, TestContext.Current.CancellationToken);

        // Assert
        (await ReadHostPausesAsync()).ShouldHaveSingleItem().ProbeAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task WhenNoRepositoryCanBeProbed_RecordsTheProbeAsUnreachable()
    {
        // Arrange — one repository has no usable token, and no other is monitored on the host.
        MonitoredRepositoryId tokenless = MonitoredRepositoryId.New();
        _repositoriesOnHost.Add(tokenless);
        _dispatchInfo[tokenless] = DispatchInfo("owner/tokenless", token: null);

        // Act
        await BuildProber().ProbeAsync(Host, TestContext.Current.CancellationToken);

        // Assert
        _publisher.ProbeRequests.ShouldBeEmpty();
        (await ReadHostPausesAsync()).ShouldHaveSingleItem().ProbeAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task WhenARepositoryHasNoToken_ItIsSkippedWithoutCountingTowardTheLimit()
    {
        // Arrange
        MonitoredRepositoryId tokenless = MonitoredRepositoryId.New();
        _repositoriesOnHost.Add(tokenless);
        _dispatchInfo[tokenless] = DispatchInfo("owner/tokenless", token: null);
        AddRepository("owner/a");
        AddRepository("owner/b");
        AddRepository("owner/c");
        _publisher.WithProbeOutcomes(
            new HostProbeOutcome.Unreachable("dns"),
            new HostProbeOutcome.Unreachable("dns"),
            new HostProbeOutcome.Unreachable("dns"));

        // Act
        await BuildProber().ProbeAsync(Host, TestContext.Current.CancellationToken);

        // Assert
        _publisher.ProbeRequests.Select(r => r.CloneUrl.AbsolutePath).ShouldBe(["/owner/a.git", "/owner/b.git", "/owner/c.git"]);
    }

    [Fact]
    public async Task WhenProbing_UsesTheRepositorysCloneUrlAndToken()
    {
        // Arrange
        AddRepository("owner/a");

        // Act
        await BuildProber().ProbeAsync(Host, TestContext.Current.CancellationToken);

        // Assert
        _publisher.ProbeRequests.ShouldHaveSingleItem().ShouldBe(
            new HostProbeRequest(new Uri($"https://{Host}/owner/a.git"), "token-owner/a"));
    }

    [Fact]
    public async Task WhenRanking_TheMostRecentlyPreparedRepositoryIsProbedFirstAndNeverPreparedOnesLast()
    {
        // Arrange — dispatch order a, b, c, d; b was prepared long ago, d most recently, a and c never.
        MonitoredRepositoryId a = AddRepository("owner/a");
        MonitoredRepositoryId b = AddRepository("owner/b");
        AddRepository("owner/c");
        MonitoredRepositoryId d = AddRepository("owner/d");
        await SeedPreparedRunAsync(b, DateTimeOffset.UtcNow.AddDays(-3));
        await SeedPreparedRunAsync(d, DateTimeOffset.UtcNow.AddHours(-1));
        await SeedPreparedRunAsync(b, DateTimeOffset.UtcNow.AddDays(-2));
        _publisher.WithProbeOutcomes(
            new HostProbeOutcome.Unreachable("dns"),
            new HostProbeOutcome.Unreachable("dns"),
            new HostProbeOutcome.Unreachable("dns"));

        // Act
        await BuildProber().ProbeAsync(Host, TestContext.Current.CancellationToken);

        // Assert
        _publisher.ProbeRequests.Select(r => r.CloneUrl.AbsolutePath).ShouldBe(["/owner/d.git", "/owner/b.git", "/owner/a.git"]);
        _ = a;
    }

    [Fact]
    public async Task WhenThePauseWasClearedDuringTheProbe_NothingIsSavedOrBroadcast()
    {
        // Arrange
        AddRepository("owner/a");
        _publisher.WithProbeOutcomes(new HostProbeOutcome.Unreachable("dns"));
        GlobalSettings settings = await _dbContext.Set<GlobalSettings>().SingleAsync(TestContext.Current.CancellationToken);
        settings.ResumeAllDispatch();
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        await BuildProber().ProbeAsync(Host, TestContext.Current.CancellationToken);

        // Assert
        (await ReadHostPausesAsync()).ShouldBeEmpty();
        _broadcaster.SentNotifications.ShouldBeEmpty();
    }

    private HostProber BuildProber() => new(
        _dbContext,
        new StubHostRepositoryQueries(_repositoriesOnHost),
        new DictionaryRepositoryDispatchQueries(_dispatchInfo),
        _publisher,
        _broadcaster,
        new FixedTimeProvider(Now),
        NullLogger<HostProber>.Instance);

    private MonitoredRepositoryId AddRepository(string slug)
    {
        MonitoredRepositoryId id = MonitoredRepositoryId.New();
        _repositoriesOnHost.Add(id);
        _dispatchInfo[id] = DispatchInfo(slug, token: $"token-{slug}");
        return id;
    }

    private static RepositoryDispatchInfo DispatchInfo(string slug, string? token) => new(
        slug,
        new Uri($"https://{Host}/{slug}.git"),
        token,
        new WorkerProvider.GitLab(),
        $"https://{Host}/api/v4/projects/x/issues",
        Host);

    private async Task SeedPreparedRunAsync(MonitoredRepositoryId repositoryId, DateTimeOffset createdAt)
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordPreparedBase(new RunBase(CommitSha.From(new string('1', 40)), RunBranchHead: null), repositoryId);
        FailedRun failed = starting.Fail(new FailureReason.ContainerError("start failed"));
        _dbContext.Set<WorkerRun>().Add(failed);
        _dbContext.Entry(failed).Property(nameof(WorkerRun.CreatedAt)).CurrentValue = createdAt;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<IReadOnlyList<HostPause>> ReadHostPausesAsync()
    {
        _dbContext.ChangeTracker.Clear();
        GlobalSettings settings = await _dbContext.Set<GlobalSettings>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        return settings.HostPauses;
    }

    private void SentNotificationShouldBeDispatch(bool isActive)
    {
        Foundry.Shared.SystemNotification sent = _broadcaster.SentNotifications.ShouldHaveSingleItem();
        sent.ShouldSatisfyAllConditions(
            () => sent.Category.ShouldBe("dispatch"),
            () => sent.IsActive.ShouldBe(isActive));
    }

    private sealed class StubHostRepositoryQueries(IReadOnlyList<MonitoredRepositoryId> repositories) : IHostRepositoryQueries
    {
        public Task<IReadOnlyList<MonitoredRepositoryId>> GetRepositoryIdsOnHostAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MonitoredRepositoryId>>(host == Host ? [.. repositories] : []);
    }

    private sealed class DictionaryRepositoryDispatchQueries(
        IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDispatchInfo?> infos) : IRepositoryDispatchQueries
    {
        public Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult(infos.GetValueOrDefault(repositoryId));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
