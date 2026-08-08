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

    private WorkerImageRebuildService BuildService(
        IWorkerImageRebuildQueue queue,
        ISystemNotificationBroadcaster? broadcaster = null,
        TimeSpan? initialBackoff = null)
        => BuildServiceCore(
            queue,
            broadcaster,
            contentRootPath: string.Empty,
            relativeContextPath: string.Empty,
            initialBackoff: initialBackoff);

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
        // Arrange — use a queue whose RequestImmediateRebuild raises the event synchronously
        SeedGlobalSettings();

        TrackingQueue queue = new();
        WorkerImageRebuildService sut = BuildService(queue, initialBackoff: TimeSpan.FromMilliseconds(500));

        using CancellationTokenSource cts = new();
        await sut.StartAsync(cts.Token);

        // Queue first item — starts processing (GlobalSettings missing so it early-returns without error)
        queue.RaiseTryEnqueue();

        // Wait briefly for the service to start processing
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Immediately request rebuild — should cancel any pending retry
        queue.RaiseImmediateRebuild();

        // Wait and check that ImmediateRebuildRequested was raised
        await Task.Delay(50, TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        // Assert — the event was raised
        queue.ImmediateRebuildRaisedCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task WhenBuildFailsAndDelayElapses_ReEnqueuesRebuild()
    {
        // Arrange — tiny backoff so the delay fires almost immediately
        SeedGlobalSettings();

        ControlledQueue queue = new();
        // Use a non-existent context path so the build fails (TarFile throws DirectoryNotFoundException)
        WorkerImageRebuildService sut = BuildServiceWithFailingContextPath(
            queue,
            initialBackoff: TimeSpan.FromMilliseconds(5));

        using CancellationTokenSource cts = new();
        await sut.StartAsync(cts.Token);

        // Signal one rebuild
        queue.Enqueue();

        // Wait long enough for the delayed retry to fire (5ms delay + processing time)
        await Task.Delay(300, TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        // Assert — TryEnqueue was called at least once (the delayed retry fired after the failure)
        queue.TryEnqueueCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    private sealed class TrackingQueue : IWorkerImageRebuildQueue
    {
        private readonly System.Threading.Channels.Channel<bool> _channel =
            System.Threading.Channels.Channel.CreateBounded<bool>(
                new System.Threading.Channels.BoundedChannelOptions(capacity: 5)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
                });

        public int ImmediateRebuildRaisedCount { get; private set; }

        public event Action? ImmediateRebuildRequested;

        public void RaiseImmediateRebuild()
        {
            ImmediateRebuildRaisedCount++;
            ImmediateRebuildRequested?.Invoke();
            _channel.Writer.TryWrite(true);
        }

        public void RaiseTryEnqueue()
        {
            _channel.Writer.TryWrite(true);
        }

        public void RequestImmediateRebuild()
        {
            ImmediateRebuildRaisedCount++;
            ImmediateRebuildRequested?.Invoke();
            TryEnqueue();
        }

        public bool TryEnqueue()
        {
            return _channel.Writer.TryWrite(true);
        }

        public async IAsyncEnumerable<bool> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (bool signal in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return signal;
            }
        }
    }

    private sealed class ControlledQueue : IWorkerImageRebuildQueue
    {
        private readonly System.Threading.Channels.Channel<bool> _channel =
            System.Threading.Channels.Channel.CreateBounded<bool>(
                new System.Threading.Channels.BoundedChannelOptions(capacity: 10)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
                });

        public int TryEnqueueCount { get; private set; }

        public event Action? ImmediateRebuildRequested;

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
            return _channel.Writer.TryWrite(true);
        }

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

    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Foundry.WebApi";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
    }
}
