using System.Threading;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildServiceTests;

public sealed class ExecuteAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public ExecuteAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private void SeedGlobalSettings()
    {
        using FoundryDbContext db = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();
        db.Set<GlobalSettings>().Add(settings);
        db.SaveChanges();
    }

    private WorkerImageRebuildService BuildService(
        IWorkerImageRebuildQueue queue,
        ISystemNotificationBroadcaster broadcaster,
        CapturingLogger logger)
    {
        SqliteConnection connection = _connection;

        ServiceCollection services = new();
        services.AddScoped<FoundryDbContext>(_ =>
        {
            DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
                .UseSqlite(connection)
                .Options;
            return new FoundryDbContext(options);
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        ServiceProvider sp = services.BuildServiceProvider();

        WorkerOptions workerOptions = new()
        {
            Image = "test-image:latest",
            ImageBuild = new ImageBuildOptions
            {
                Enabled = true,
                ContextPath = string.Empty,
            },
        };

        return new WorkerImageRebuildService(
            queue,
            sp.GetRequiredService<IServiceScopeFactory>(),
            new NullImageOperations(),
            new StubHostEnvironment(string.Empty),
            Options.Create(workerOptions),
            broadcaster,
            new CapturingLoggerAdapter<WorkerImageRebuildService>(logger));
    }

    [Fact]
    public async Task WhenFirstItemBodyThrows_ErrorIsLoggedAndSecondItemIsStillProcessed()
    {
        // Arrange
        SeedGlobalSettings();

        CapturingLogger logger = new();
        InvalidOperationException firstItemException = new("broadcaster failure on item 1");
        int broadcastCallCount = 0;
        FaultOnFirstCallBroadcaster broadcaster = new(firstItemException, onSendAsync: () => broadcastCallCount++);
        TwoItemQueue queue = new();

        WorkerImageRebuildService sut = BuildService(queue, broadcaster, logger);

        using CancellationTokenSource cts = new();

        // Act — run the consumer loop; the queue yields two items then completes
        await sut.StartAsync(cts.Token);
        await queue.Completed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        // Assert — error logged for the first item
        logger.Entries.ShouldContain(
            e => e.Level == LogLevel.Error && e.Exception == firstItemException,
            "consumer loop must log an Error when the item body throws");

        // Assert — second item reached the broadcaster (broadcastCallCount >= 2: first item throws on 1st call,
        // second item makes at least one successful call)
        broadcastCallCount.ShouldBeGreaterThanOrEqualTo(2,
            "the second queued item must be processed after the first item throws");
    }

    [Fact]
    public async Task WhenStoppingTokenCancelled_LoopExitsWithNoErrorLogged()
    {
        // Arrange
        SeedGlobalSettings();

        CapturingLogger logger = new();
        NeverCalledBroadcaster broadcaster = new();
        BlockingQueue queue = new();

        WorkerImageRebuildService sut = BuildService(queue, broadcaster, logger);

        using CancellationTokenSource cts = new();

        // Act — start the service, then immediately cancel before any item is dequeued
        await sut.StartAsync(cts.Token);
        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        // Assert
        logger.Entries.ShouldNotContain(
            e => e.Level == LogLevel.Error,
            "clean shutdown cancellation must not produce any Error log entries");
    }

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    /// <summary>
    /// Yields two items synchronously, then signals completion.
    /// </summary>
    private sealed class TwoItemQueue : IWorkerImageRebuildQueue
    {
        public SemaphoreSlim Completed { get; } = new(0, 1);

        public event Action? ImmediateRebuildRequested;

        public void RequestImmediateRebuild()
        {
            ImmediateRebuildRequested?.Invoke();
            TryEnqueue();
        }

        public bool TryEnqueue() => false;

        public async IAsyncEnumerable<bool> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return true;
            yield return true;
            await Task.CompletedTask;
            Completed.Release();
        }
    }

    /// <summary>
    /// Blocks until cancellation — simulates a queue that never yields.
    /// </summary>
    private sealed class BlockingQueue : IWorkerImageRebuildQueue
    {
        public event Action? ImmediateRebuildRequested;

        public void RequestImmediateRebuild()
        {
            ImmediateRebuildRequested?.Invoke();
            TryEnqueue();
        }

        public bool TryEnqueue() => false;

        public async IAsyncEnumerable<bool> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(10, CancellationToken.None);
            }

            yield break;
        }
    }

    /// <summary>
    /// Throws on the first SendAsync call to simulate a broadcaster failure that escapes the inner
    /// Docker build try/catch in ProcessRebuildAsync. Subsequent calls succeed and increment a counter.
    /// </summary>
    private sealed class FaultOnFirstCallBroadcaster(
        Exception firstCallException,
        Action onSendAsync) : ISystemNotificationBroadcaster
    {
        private int _callCount;

        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _callCount);
            onSendAsync();

            if (call == 1)
            {
                throw firstCallException;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class NeverCalledBroadcaster : ISystemNotificationBroadcaster
    {
        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Adapts a non-generic CapturingLogger to ILogger&lt;T&gt; so the service can be constructed with it.
    /// </summary>
    private sealed class CapturingLoggerAdapter<T>(CapturingLogger inner) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }

    private sealed class NullImageOperations : IImageOperations
    {
        public Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken) => Task.CompletedTask;

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(
            Stream contents,
            ImageBuildParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

        public Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImagesListResponse>>([]);
        public Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult(new ImageInspectResponse());
        public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<IDictionary<string, string>>>([]);
        public Task<IList<ImageSearchResponse>> SearchImagesAsync(ImagesSearchParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImageSearchResponse>>([]);
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadImageAsync(ImageLoadParameters parameters, Stream imageStream, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> SaveImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> SaveImagesAsync(string[] names, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task TagImageAsync(string name, ImageTagParameters parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PushImageAsync(string name, ImagePushParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ImagesPruneResponse> PruneImagesAsync(ImagesPruneParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new ImagesPruneResponse());
        public Task<CommitContainerChangesResponse> CommitContainerChangesAsync(CommitContainerChangesParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new CommitContainerChangesResponse());
        public Task<IList<ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken) => Task.FromResult<IList<ImageHistoryResponse>>([]);
    }

    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Foundry.WebApi";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
    }

}
