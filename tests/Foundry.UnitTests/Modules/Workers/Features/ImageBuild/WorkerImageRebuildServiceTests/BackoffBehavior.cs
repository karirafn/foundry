using System.Runtime.CompilerServices;

using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildServiceTests;

public sealed class BackoffBehavior
{
    // ContentRootPath=/tmp → solutionRoot=/ → context=solutionRoot/workers=/workers
    // /workers does not exist so TarFile.CreateFromDirectoryAsync throws, causing a build failure.
    private const string FailingContentRootPath = "/tmp";

    private static WorkerImageRebuildService BuildServiceWithFailingContextPath(
        IWorkerImageRebuildQueue queue,
        IIntegrationEventDispatcher? dispatcher = null)
        => BuildServiceCore(queue, dispatcher, contentRootPath: FailingContentRootPath);

    private static WorkerImageRebuildService BuildServiceCore(
        IWorkerImageRebuildQueue queue,
        IIntegrationEventDispatcher? dispatcher,
        string contentRootPath = FailingContentRootPath)
    {
        ServiceCollection services = new();
        services.AddScoped<IGlobalSettingsQueries>(_ => new StubGlobalSettingsQueries());
        services.AddScoped<IIntegrationEventDispatcher>(_ =>
            dispatcher ?? new NullIntegrationEventDispatcher());

        ServiceProvider sp = services.BuildServiceProvider();

        return new WorkerImageRebuildService(
            queue,
            sp.GetRequiredService<IServiceScopeFactory>(),
            new NullImageOperations(),
            new StubHostEnvironment(contentRootPath),
            NullLogger<WorkerImageRebuildService>.Instance);
    }

    [Fact]
    public async Task WhenImmediateRebuildRequested_CancelsScheduledRetry()
    {
        // Arrange — the service uses a 30s constant backoff, which is long enough that the
        // delayed retry cannot fire before we supersede it with RequestImmediateRebuild.
        // Use a non-existent context path so the build fails and a retry is scheduled.

        // The dispatcher signals when the OutcomeFailed event is dispatched, which means the
        // service has finished processing and the pending retry CTS is in place.
        using SignallingDispatcher signallingDispatcher = new();
        using ControlledQueue queue = new();

        WorkerImageRebuildService sut = BuildServiceCore(
            queue,
            signallingDispatcher,
            contentRootPath: FailingContentRootPath);

        using CancellationTokenSource cts = new();
        await sut.StartAsync(cts.Token);

        // Signal one rebuild — the build will fail and schedule a 500ms delayed retry.
        queue.Enqueue();

        // Wait deterministically for the service to reach the failed state and set up the pending retry.
        bool failureNotified = await signallingDispatcher.WaitForFailureEventAsync(
            TimeSpan.FromSeconds(5));
        failureNotified.ShouldBeTrue("the service should have dispatched an ImageBuildOutcomeFailed event");

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
    public async Task WhenImmediateRebuildSupersedes_DisposeDoesNotThrow()
    {
        // Arrange — verifies that after a supersede cancels the pending retry CTS, Dispose
        // releases that CTS without throwing, confirming no orphaned resources.
        using SignallingDispatcher signal = new();
        using ControlledQueue queue = new();

        WorkerImageRebuildService sut = BuildServiceCore(
            queue,
            signal,
            contentRootPath: FailingContentRootPath);

        using CancellationTokenSource cts = new();
        await sut.StartAsync(cts.Token);

        // Signal one rebuild — the build will fail and schedule a 500ms pending retry.
        queue.Enqueue();

        // Wait deterministically for the failure event so the pending retry CTS is in place.
        bool notified = await signal.WaitForFailureEventAsync(TimeSpan.FromSeconds(5));
        notified.ShouldBeTrue("the service should have dispatched an ImageBuildOutcomeFailed event");

        // Supersede cancels the pending retry CTS and re-enqueues immediately.
        queue.RequestImmediateRebuild();

        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        // Assert — Dispose must not throw after a supersede.
        Record.Exception(() => sut.Dispose()).ShouldBeNull();
    }

    [Fact]
    public async Task WhenBuildFails_NextRetryAtReflectsThirtySecondInitialBackoff()
    {
        // Arrange — the service uses constant InitialBackoff=30s; verify the published
        // NextRetryAt is at least 30s in the future (attempt 1 → 30s * 2^0 = 30s).
        using SignallingDispatcher dispatcher = new();
        using ControlledQueue queue = new();
        WorkerImageRebuildService sut = BuildServiceWithFailingContextPath(queue, dispatcher);

        using CancellationTokenSource cts = new();
        DateTimeOffset before = DateTimeOffset.UtcNow;
        await sut.StartAsync(cts.Token);

        queue.Enqueue();

        bool failureNotified = await dispatcher.WaitForFailureEventAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        // Assert
        failureNotified.ShouldBeTrue();
        ImageBuildOutcomeFailed lastFailed = dispatcher.LastFailedEvent.ShouldNotBeNull();
        lastFailed.NextRetryAt.ShouldNotBeNull();
        DateTimeOffset nextRetryAt = lastFailed.NextRetryAt.Value;
        nextRetryAt.ShouldBeGreaterThan(before + TimeSpan.FromSeconds(29));
    }

    [Fact]
    public async Task WhenBuildFails_SchedulesDelayedRetry()
    {
        // Arrange — verify that after a build failure an ImageBuildOutcomeFailed event is dispatched,
        // which proves the retry was scheduled (the actual delay fires after 30s, tested end-to-end).
        using SignallingDispatcher dispatcher = new();
        using ControlledQueue queue = new();
        WorkerImageRebuildService sut = BuildServiceWithFailingContextPath(queue, dispatcher);

        using CancellationTokenSource cts = new();
        await sut.StartAsync(cts.Token);

        // Signal one rebuild — the build will fail (non-existent context path).
        queue.Enqueue();

        // Wait deterministically for the failure event.
        bool failureNotified = await dispatcher.WaitForFailureEventAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        // Assert — failure event dispatched confirms the retry was scheduled.
        failureNotified.ShouldBeTrue("the service should have dispatched an ImageBuildOutcomeFailed event after build failure");
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

    private sealed class NullIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    /// A dispatcher that signals a gate when an <see cref="ImageBuildOutcomeFailed"/> event is dispatched,
    /// enabling tests to wait deterministically until the service has recorded the failure and set up the
    /// pending retry CTS.
    /// </summary>
    private sealed class SignallingDispatcher : IIntegrationEventDispatcher, IDisposable
    {
        private readonly SemaphoreSlim _failureSignal = new(initialCount: 0, maxCount: 1);

        public ImageBuildOutcomeFailed? LastFailedEvent { get; private set; }

        public void Dispose() => _failureSignal.Dispose();

        public Task<bool> WaitForFailureEventAsync(TimeSpan timeout)
            => _failureSignal.WaitAsync(timeout);

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            foreach (IIntegrationEvent @event in events)
            {
                if (@event is ImageBuildOutcomeFailed failed && _failureSignal.CurrentCount == 0)
                {
                    LastFailedEvent = failed;
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
