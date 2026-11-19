using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.PackageProxyPauses;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

using Foundry.UnitTests.Modules.Settings.Features;

namespace Foundry.UnitTests.Modules.Workers.Features.PackageProxyPauses.PackageProxyProbeServiceTests;

public sealed class ExecuteTickAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset PausedAt = new(2026, 11, 19, 7, 39, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly BlockingProbe _probe = new();

    public ExecuteTickAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using FoundryDbContext db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _probe.Release();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenTheProbeIsDue_ProbesTheProxyOnce()
    {
        // Arrange
        SeedSettings(paused: true);
        _probe.Release();
        await using ServiceProvider services = BuildServices(now: PausedAt.AddMinutes(1));

        // Act
        await BuildService(services).ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        _probe.ProbeCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTheProbeIsNotYetDue_DoesNotProbe()
    {
        // Arrange
        SeedSettings(paused: true);
        await using ServiceProvider services = BuildServices(now: PausedAt.AddSeconds(59));

        // Act
        await BuildService(services).ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        _probe.ProbeCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenTheProxyIsNotPaused_DoesNotProbe()
    {
        // Arrange
        SeedSettings(paused: false);
        await using ServiceProvider services = BuildServices(now: PausedAt.AddHours(1));

        // Act
        await BuildService(services).ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        _probe.ProbeCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenDispatchIsGloballyPaused_StillProbes()
    {
        // Arrange
        SeedSettings(paused: true, globallyPaused: true);
        _probe.Release();
        await using ServiceProvider services = BuildServices(now: PausedAt.AddMinutes(1));

        // Act
        await BuildService(services).ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        _probe.ProbeCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTwoTicksOverlap_OnlyOneProbeRuns()
    {
        // Arrange
        SeedSettings(paused: true);
        await using ServiceProvider services = BuildServices(now: PausedAt.AddMinutes(1));
        PackageProxyProbeService sut = BuildService(services);
        Task first = sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await _probe.FirstProbeStarted.WaitAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        _probe.Release();
        await first;

        // Assert
        _probe.ProbeCount.ShouldBe(1);
    }

    private void SeedSettings(bool paused, bool globallyPaused = false)
    {
        using FoundryDbContext db = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();
        if (paused)
        {
            settings.PausePackageProxy(PausedAt);
        }

        if (globallyPaused)
        {
            settings.PauseDispatch();
        }

        db.Set<GlobalSettings>().Add(settings);
        db.SaveChanges();
    }

    private ServiceProvider BuildServices(DateTimeOffset now)
    {
        ServiceCollection services = new();
        services.AddDbContext<FoundryDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddScoped<IGlobalSettingsQueries, GlobalSettingsQueries>();
        services.AddSingleton<IWebhookIngressProbe>(new StubWebhookIngressProbe());
        services.AddSingleton<IPackageSourceProxyAddress>(new StubProxyAddress());
        services.AddSingleton<IWorkerHostEntryResolver>(new StubHostEntryResolver());
        services.AddSingleton<IPackageProxyProbe>(_probe);
        services.AddSingleton<ISystemNotificationBroadcaster>(new CapturingSystemNotificationBroadcaster());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(now));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<PackageProxyProber>();
        return services.BuildServiceProvider();
    }

    private static PackageProxyProbeService BuildService(ServiceProvider services) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        services.GetRequiredService<TimeProvider>(),
        NullLogger<PackageProxyProbeService>.Instance);

    private FoundryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    private sealed class StubProxyAddress : IPackageSourceProxyAddress
    {
        public Uri WorkerBaseUrl => new("http://host.docker.internal:5287/package-sources/");
    }

    private sealed class StubHostEntryResolver : IWorkerHostEntryResolver
    {
        public Task<string> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult("host.docker.internal:host-gateway");
    }

    /// <summary>Holds every probe until released, counting them.</summary>
    private sealed class BlockingProbe : IPackageProxyProbe
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstProbeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _probeCount;

        public Task FirstProbeStarted => _firstProbeStarted.Task;

        public int ProbeCount => Volatile.Read(ref _probeCount);

        public void Release() => _release.TrySetResult();

        public async Task<PackageProxyProbeOutcome> ProbeAsync(Uri proxyUrl, string extraHostEntry, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _probeCount);
            _firstProbeStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new PackageProxyProbeOutcome.Unreachable("Connection refused");
        }
    }
}
