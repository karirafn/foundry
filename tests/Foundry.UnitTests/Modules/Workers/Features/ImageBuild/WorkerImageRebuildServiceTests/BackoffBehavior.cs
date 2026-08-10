using System.Runtime.CompilerServices;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildServiceTests;

public sealed class BackoffBehavior : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public BackoffBehavior()
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

    private WorkerImageRebuildService BuildServiceWithFailingContextPath(
        IWorkerImageRebuildQueue queue,
        TimeSpan? initialBackoff = null)
        => BuildServiceCore(
            queue,
            broadcaster: null,
            // ContentRootPath=/tmp → solutionRoot=/ → resolved contextPath=/nonexistent-path-for-test
            contentRootPath: "/tmp",
            relativeContextPath: "nonexistent-path-for-test",
            initialBackoff: initialBackoff);

    private WorkerImageRebuildService BuildServiceCore(
        IWorkerImageRebuildQueue queue,
        ISystemNotificationBroadcaster? broadcaster,
        string contentRootPath,
        string relativeContextPath,
        TimeSpan? initialBackoff)
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
                ContextPath = relativeContextPath,
                InitialBackoff = initialBackoff ?? TimeSpan.FromMilliseconds(1),
                MaxBackoff = TimeSpan.FromMilliseconds(100),
            },
        };

        return new WorkerImageRebuildService(
            queue,
            sp.GetRequiredService<IServiceScopeFactory>(),
            new NullImageOperations(),
            new StubHostEnvironment(contentRootPath),
            Options.Create(workerOptions),
            broadcaster ?? new NullNotificationBroadcaster(),
            NullLogger<WorkerImageRebuildService>.Instance);
    }

    [Fact]
    public async Task WhenImmediateRebuildRequested_CancelsScheduledRetry()
    {
        // Arrange — long backoff (500ms) so the delayed retry cannot fire before we supersede it.
        // Use a non-existent context path so the build fails and a retry is scheduled.
        SeedGlobalSettings();

        // The broadcaster signals when the failure notification is sent, which means the
        // service has finished processing and the pending retry CTS is in place.
        using SignallingBroadcaster broadcaster = new();
        using ControlledQueue queue = new();

        WorkerImageRebuildService sut = BuildServiceCore(
            queue,
            broadcaster,
            contentRootPath: "/tmp",
            relativeContextPath: "nonexistent-path-for-test",
            initialBackoff: TimeSpan.FromMilliseconds(500));

        using CancellationTokenSource cts = new();
        await sut.StartAsync(cts.Token);

        // Signal one rebuild — the build will fail and schedule a 500ms delayed retry.
        queue.Enqueue();

        // Wait deterministically for the service to reach the failed state and set up the pending retry.
        bool failureNotified = await broadcaster.WaitForFailureNotificationAsync(
            TimeSpan.FromSeconds(5));
        failureNotified.ShouldBeTrue("the service should have sent a failure notification");

        // Capture the TryEnqueue count before the supersede. At this point, only the internal
        // ScheduleDelayedRetryAsync is running; no TryEnqueue should have been called yet.
        int countBeforeSupersede = queue.TryEnqueueCount;

        // Supersede the pending retry — this cancels the 500ms delayed re-enqueue
        // and re-enqueues immediately (calling TryEnqueue once itself).
        queue.RequestImmediateRebuild();

        // Stop the service immediately so the second build cycle (triggered by RequestImmediateRebuild)
        // does not start and cause its own delayed retry to fire.
        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        // Assert — TryEnqueue was called exactly once (by RequestImmediateRebuild itself),
        // not by the cancelled delayed retry task.
        queue.TryEnqueueCount.ShouldBe(countBeforeSupersede + 1,
            "only the immediate re-enqueue from RequestImmediateRebuild should have fired");
    }

    [Fact]
    public async Task WhenBuildFailsAndDelayElapses_ReEnqueuesRebuild()
    {
        // Arrange — tiny backoff so the delay fires almost immediately
        SeedGlobalSettings();

        using ControlledQueue queue = new();
        // Use a non-existent context path so the build fails (TarFile throws DirectoryNotFoundException)
        WorkerImageRebuildService sut = BuildServiceWithFailingContextPath(
            queue,
            initialBackoff: TimeSpan.FromMilliseconds(5));

        using CancellationTokenSource cts = new();
        await sut.StartAsync(cts.Token);

        // Signal one rebuild
        queue.Enqueue();

        // Wait deterministically for the delayed retry to fire (5ms backoff).
        bool retryFired = await queue.WaitForTryEnqueueAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        // Assert — TryEnqueue was called once (the delayed retry fired after the failure)
        retryFired.ShouldBeTrue("the delayed retry should have re-enqueued a rebuild after the backoff elapsed");
    }

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    private sealed class ControlledQueue : IWorkerImageRebuildQueue, IDisposable
    {
        private readonly System.Threading.Channels.Channel<bool> _channel =
            System.Threading.Channels.Channel.CreateBounded<bool>(
                new System.Threading.Channels.BoundedChannelOptions(capacity: 10)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
                });

        // Released once inside TryEnqueue so waiters are unblocked deterministically.
        private readonly SemaphoreSlim _tryEnqueueSignal = new(initialCount: 0, maxCount: int.MaxValue);

        public int TryEnqueueCount { get; private set; }

        public event Action? ImmediateRebuildRequested;

        public void Dispose() => _tryEnqueueSignal.Dispose();

        public void Enqueue()
        {
            _channel.Writer.TryWrite(true);
        }

        public void RequestImmediateRebuild()
        {
            ImmediateRebuildRequested?.Invoke();
            TryEnqueue();
        }

        public bool TryEnqueue()
        {
            TryEnqueueCount++;
            _tryEnqueueSignal.Release();
            return _channel.Writer.TryWrite(true);
        }

        /// <summary>
        /// Waits deterministically until <see cref="TryEnqueue"/> is called or the timeout elapses.
        /// Returns <c>true</c> if <see cref="TryEnqueue"/> fired within the timeout; <c>false</c> otherwise.
        /// </summary>
        public Task<bool> WaitForTryEnqueueAsync(TimeSpan timeout)
            => _tryEnqueueSignal.WaitAsync(timeout);

        public async IAsyncEnumerable<bool> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (bool signal in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return signal;
            }
        }
    }

    private sealed class NullImageOperations : Docker.DotNet.IImageOperations
    {
        public Task BuildImageFromDockerfileAsync(
            Docker.DotNet.Models.ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<Docker.DotNet.Models.AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<Docker.DotNet.Models.JSONMessage> progress,
            CancellationToken cancellationToken) => Task.CompletedTask;

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(
            Stream contents,
            Docker.DotNet.Models.ImageBuildParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

        public Task<IList<Docker.DotNet.Models.ImagesListResponse>> ListImagesAsync(Docker.DotNet.Models.ImagesListParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<Docker.DotNet.Models.ImagesListResponse>>([]);
        public Task<Docker.DotNet.Models.ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult(new Docker.DotNet.Models.ImageInspectResponse());
        public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, Docker.DotNet.Models.ImageDeleteParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<IDictionary<string, string>>>([]);
        public Task<IList<Docker.DotNet.Models.ImageSearchResponse>> SearchImagesAsync(Docker.DotNet.Models.ImagesSearchParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<Docker.DotNet.Models.ImageSearchResponse>>([]);
        public Task CreateImageAsync(Docker.DotNet.Models.ImagesCreateParameters parameters, Docker.DotNet.Models.AuthConfig authConfig, IProgress<Docker.DotNet.Models.JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(Docker.DotNet.Models.ImagesCreateParameters parameters, Docker.DotNet.Models.AuthConfig authConfig, IDictionary<string, string> headers, IProgress<Docker.DotNet.Models.JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(Docker.DotNet.Models.ImagesCreateParameters parameters, Stream imageStream, Docker.DotNet.Models.AuthConfig authConfig, IProgress<Docker.DotNet.Models.JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(Docker.DotNet.Models.ImagesCreateParameters parameters, Stream imageStream, Docker.DotNet.Models.AuthConfig authConfig, IDictionary<string, string> headers, IProgress<Docker.DotNet.Models.JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadImageAsync(Docker.DotNet.Models.ImageLoadParameters parameters, Stream imageStream, IProgress<Docker.DotNet.Models.JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> SaveImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> SaveImagesAsync(string[] names, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task TagImageAsync(string name, Docker.DotNet.Models.ImageTagParameters parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PushImageAsync(string name, Docker.DotNet.Models.ImagePushParameters parameters, Docker.DotNet.Models.AuthConfig authConfig, IProgress<Docker.DotNet.Models.JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Docker.DotNet.Models.ImagesPruneResponse> PruneImagesAsync(Docker.DotNet.Models.ImagesPruneParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new Docker.DotNet.Models.ImagesPruneResponse());
        public Task<Docker.DotNet.Models.CommitContainerChangesResponse> CommitContainerChangesAsync(Docker.DotNet.Models.CommitContainerChangesParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new Docker.DotNet.Models.CommitContainerChangesResponse());
        public Task<IList<Docker.DotNet.Models.ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken) => Task.FromResult<IList<Docker.DotNet.Models.ImageHistoryResponse>>([]);
    }

    private sealed class NullNotificationBroadcaster : ISystemNotificationBroadcaster
    {
        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    /// A broadcaster that signals a gate when the failure notification is sent, enabling tests to wait
    /// deterministically until the service has recorded the failure and set up the pending retry CTS.
    ///
    /// A failed build emits exactly two active image-build broadcasts: building first, then failed.
    /// The gate releases on the second active image-build broadcast, which is the failure notification.
    /// Gating on message content is avoided because messages are now empty (pure reload trigger).
    /// </summary>
    private sealed class SignallingBroadcaster : ISystemNotificationBroadcaster, IDisposable
    {
        // Released when the second active image-build broadcast arrives (the failed notification).
        private readonly SemaphoreSlim _failureSignal = new(initialCount: 0, maxCount: 1);
        private int _activeImageBuildCount;

        public void Dispose() => _failureSignal.Dispose();

        public Task<bool> WaitForFailureNotificationAsync(TimeSpan timeout)
            => _failureSignal.WaitAsync(timeout);

        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
        {
            if (notification.Category == WorkerImageRebuildService.ImageBuildCategory
                && notification.IsActive)
            {
                int count = Interlocked.Increment(ref _activeImageBuildCount);

                // The second active broadcast is the failure notification.
                // Release idempotently — only one failure notification is sent per build attempt.
                if (count == 2 && _failureSignal.CurrentCount == 0)
                {
                    _failureSignal.Release();
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Foundry.WebApi";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
    }
}
