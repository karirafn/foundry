using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.PackageProxyPauses;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.PackageProxyPauses.PackageProxyProberTests;

public sealed class ProbeAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset PausedAt = new(2026, 11, 19, 7, 39, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ProbedAt = PausedAt.AddMinutes(1);
    private static readonly Uri ProxyUrl = new("http://host.docker.internal:5287/package-sources/");
    private const string ExtraHost = "host.docker.internal:host-gateway";

    private readonly SqliteConnection _connection;
    private readonly CapturingSystemNotificationBroadcaster _broadcaster = new();

    public ProbeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext db = CreateDbContext();
        db.Database.EnsureCreated();
        DispatchPause settings = DispatchPause.Create();
        settings.PausePackageProxy(PausedAt);
        db.Set<DispatchPause>().Add(settings);
        db.SaveChanges();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ProbesTheWorkerBaseUrlWithTheWorkersHostEntry()
    {
        // Arrange
        StubProbe probe = new(new PackageProxyProbeOutcome.Reachable());
        await using FoundryDbContext db = CreateDbContext();

        // Act
        await BuildProber(db, probe).ProbeAsync(TestContext.Current.CancellationToken);

        // Assert
        probe.Calls.ShouldBe([(ProxyUrl, ExtraHost)]);
    }

    [Fact]
    public async Task WhenTheProxyIsReachable_ClearsThePauseAndBroadcasts()
    {
        // Arrange
        await using FoundryDbContext db = CreateDbContext();

        // Act
        await BuildProber(db, new StubProbe(new PackageProxyProbeOutcome.Reachable())).ProbeAsync(TestContext.Current.CancellationToken);

        // Assert
        (await ReadPauseAsync()).ShouldBeNull();
        _broadcaster.SentNotifications.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            n => n.Category.ShouldBe("dispatch"),
            n => n.IsActive.ShouldBeFalse());
    }

    [Theory]
    [InlineData("unreachable")]
    [InlineData("inconclusive")]
    public async Task WhenTheProbeDoesNotReachTheProxy_AdvancesTheBackoffAndBroadcasts(string result)
    {
        // Arrange
        PackageProxyProbeOutcome outcome = result == "unreachable"
            ? new PackageProxyProbeOutcome.Unreachable("Connection refused")
            : new PackageProxyProbeOutcome.Inconclusive("no line");
        await using FoundryDbContext db = CreateDbContext();

        // Act
        await BuildProber(db, new StubProbe(outcome)).ProbeAsync(TestContext.Current.CancellationToken);

        // Assert
        (await ReadPauseAsync()).ShouldBe(PackageProxyPause.Start(PausedAt).AfterUnreachableProbe(ProbedAt));
        _broadcaster.SentNotifications.ShouldHaveSingleItem().IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenResumeAllClearedThePauseDuringTheProbe_NeitherRecreatesItNorBroadcasts()
    {
        // Arrange
        await using (FoundryDbContext clearDb = CreateDbContext())
        {
            DispatchPause settings = await clearDb.Set<DispatchPause>().SingleAsync(TestContext.Current.CancellationToken);
            settings.ResumeAllDispatch();
            await clearDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext db = CreateDbContext();

        // Act
        await BuildProber(db, new StubProbe(new PackageProxyProbeOutcome.Unreachable("Connection refused")))
            .ProbeAsync(TestContext.Current.CancellationToken);

        // Assert
        (await ReadPauseAsync()).ShouldBeNull();
        _broadcaster.SentNotifications.ShouldBeEmpty();
    }

    private PackageProxyProber BuildProber(FoundryDbContext db, IPackageProxyProbe probe) => new(
        db,
        new StubProxyAddress(),
        new StubHostEntryResolver(),
        probe,
        _broadcaster,
        new FakeTimeProvider(ProbedAt),
        NullLogger<PackageProxyProber>.Instance);

    private async Task<PackageProxyPause?> ReadPauseAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        DispatchPause settings = await db.Set<DispatchPause>().SingleAsync(TestContext.Current.CancellationToken);
        return settings.PackageProxyPause;
    }

    private FoundryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    private sealed class StubProxyAddress : IPackageSourceProxyAddress
    {
        public Uri WorkerBaseUrl => ProxyUrl;
    }

    private sealed class StubHostEntryResolver : IWorkerHostEntryResolver
    {
        public Task<string> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult(ExtraHost);
    }

    private sealed class StubProbe(PackageProxyProbeOutcome outcome) : IPackageProxyProbe
    {
        public List<(Uri ProxyUrl, string ExtraHost)> Calls { get; } = [];

        public Task<PackageProxyProbeOutcome> ProbeAsync(Uri proxyUrl, string extraHostEntry, CancellationToken cancellationToken)
        {
            Calls.Add((proxyUrl, extraHostEntry));
            return Task.FromResult(outcome);
        }
    }
}
