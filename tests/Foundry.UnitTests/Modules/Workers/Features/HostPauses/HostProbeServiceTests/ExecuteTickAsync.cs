using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.HostPauses;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.HostPauses.HostProbeServiceTests;

public sealed class ExecuteTickAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset PausedAt = new(2026, 11, 13, 7, 39, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly BlockingPublisher _publisher = new();
    private readonly ServiceProvider _services;

    public ExecuteTickAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        ServiceCollection services = new();
        services.AddDbContext<FoundryDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddScoped<IGlobalSettingsQueries, GlobalSettingsQueries>();
        OneRepositoryPerHost repositories = new();
        services.AddSingleton<IHostRepositoryQueries>(repositories);
        services.AddSingleton<IRepositoryDispatchQueries>(repositories);
        services.AddSingleton<IPublisher>(_publisher);
        services.AddSingleton<ISystemNotificationBroadcaster>(new CapturingSystemNotificationBroadcaster());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(PausedAt.AddMinutes(2)));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<HostProber>();
        _services = services.BuildServiceProvider();

        using IServiceScope scope = _services.CreateScope();
        FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        db.Database.EnsureCreated();
        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseHost("due.example.test", PausedAt);
        settings.PauseHost("later.example.test", PausedAt.AddMinutes(5));
        db.Set<GlobalSettings>().Add(settings);
        db.SaveChanges();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _publisher.Release();
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenAHostsProbeIsDue_ProbesOnlyThatHost()
    {
        // Arrange
        _publisher.Release();
        HostProbeService sut = BuildService();

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        _publisher.ProbedHosts.ShouldBe(["due.example.test"]);
    }

    [Fact]
    public async Task WhenTwoTicksOverlap_OnlyOneProbeRunsForTheHost()
    {
        // Arrange
        HostProbeService sut = BuildService();
        Task first = sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await _publisher.FirstProbeStarted.WaitAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        _publisher.Release();
        await first;

        // Assert
        _publisher.ProbedHosts.ShouldBe(["due.example.test"]);
    }

    private HostProbeService BuildService() => new(
        _services.GetRequiredService<IServiceScopeFactory>(),
        _services.GetRequiredService<TimeProvider>(),
        NullLogger<HostProbeService>.Instance);

    private sealed class OneRepositoryPerHost : IHostRepositoryQueries, IRepositoryDispatchQueries
    {
        private readonly Dictionary<string, MonitoredRepositoryId> _repositories = new(StringComparer.Ordinal);

        public Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken)
        {
            lock (_repositories)
            {
                string host = _repositories.Single(entry => entry.Value == repositoryId).Key;
                return Task.FromResult<RepositoryDispatchInfo?>(new RepositoryDispatchInfo(
                    "owner/repo",
                    new Uri($"https://{host}/owner/repo.git"),
                    "glpat_token",
                    new WorkerProvider.GitLab(),
                    $"https://{host}/api/v4/projects/x/issues",
                    host));
            }
        }

        public Task<IReadOnlyList<MonitoredRepositoryId>> GetRepositoryIdsOnHostAsync(string host, CancellationToken cancellationToken)
        {
            lock (_repositories)
            {
                if (!_repositories.TryGetValue(host, out MonitoredRepositoryId id))
                {
                    id = MonitoredRepositoryId.New();
                    _repositories[host] = id;
                }

                return Task.FromResult<IReadOnlyList<MonitoredRepositoryId>>([id]);
            }
        }
    }

    /// <summary>Holds every probe until released, recording the host of each probed repository.</summary>
    private sealed class BlockingPublisher : IPublisher
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstProbeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _probedHosts = [];

        public Task FirstProbeStarted => _firstProbeStarted.Task;

        public IReadOnlyList<string> ProbedHosts
        {
            get
            {
                lock (_probedHosts)
                {
                    return [.. _probedHosts];
                }
            }
        }

        public void Release() => _release.TrySetResult();

        public async Task<HostProbeOutcome> ProbeAsync(HostProbeRequest request, CancellationToken cancellationToken)
        {
            lock (_probedHosts)
            {
                _probedHosts.Add(request.CloneUrl.Host);
            }

            _firstProbeStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new HostProbeOutcome.Unreachable("dns");
        }

        public Task<PrepareOutcome> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
