using System.Runtime.CompilerServices;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildServiceTests;

public sealed class BackoffBehavior
{
    private static WorkerImageRebuildService BuildServiceWithFailingContextPath(
        IWorkerImageRebuildQueue queue,
        IIntegrationEventDispatcher? dispatcher = null,
        TimeSpan? initialBackoff = null)
        => BuildServiceCore(
            queue,
            dispatcher,
            // ContentRootPath=/tmp → solutionRoot=/ → resolved contextPath=/nonexistent-path-for-test
            contentRootPath: "/tmp",
            relativeContextPath: "nonexistent-path-for-test",
            initialBackoff: initialBackoff);

    private static WorkerImageRebuildService BuildServiceCore(
        IWorkerImageRebuildQueue queue,
        IIntegrationEventDispatcher? dispatcher,
        string contentRootPath,
        string relativeContextPath,
        TimeSpan? initialBackoff)
    {
        ServiceCollection services = new();
        services.AddScoped<IGlobalSettingsQueries>(_ => new StubGlobalSettingsQueries());
        services.AddScoped<IIntegrationEventDispatcher>(_ =>
            dispatcher ?? new NullIntegrationEventDispatcher());

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
            NullLogger<WorkerImageRebuildService>.Instance);
    }

    [Fact]
    public async Task WhenImmediateRebuildRequested_CancelsScheduledRetry()
    {
        // Arrange — long backoff (500ms) so the delayed retry cannot fire before we supersede it.
        // Use a non-existent context path so the build fails and a retry is scheduled.

        // The dispatcher signals when the OutcomeFailed event is dispatched, which means the
        // service has finished processing and the pending retry CTS is in place.
        using SignallingDispatcher signallingDispatcher = new();
        using ControlledQueue queue = new();

        WorkerImageRebuildService sut = BuildServiceCore(
            queue,
            signallingDispatcher,
            contentRootPath: "/tmp",
            relativeContextPath: "nonexistent-path-for-test",
            initialBackoff: TimeSpan.FromMilliseconds(500));

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
    public async Task WhenBuildFailsAndDelayElapses_ReEnqueuesRebuild()
    {
        // Arrange — tiny backoff so the delay fires almost immediately
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

        public void Dispose() => _failureSignal.Dispose();

        public Task<bool> WaitForFailureEventAsync(TimeSpan timeout)
            => _failureSignal.WaitAsync(timeout);

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            foreach (IIntegrationEvent @event in events)
            {
                if (@event is ImageBuildOutcomeFailed && _failureSignal.CurrentCount == 0)
                {
                    _failureSignal.Release();
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubGlobalSettingsQueries : IGlobalSettingsQueries
    {
        private static readonly GlobalSettingsSummary DefaultSummary = new(
            MaxConcurrent: 1,
            TimeoutMinutes: 60,
            SystemPromptTemplate: null,
            WorkerPromptTemplate: null,
            UsageLimitResetsAt: null,
            IsDispatchPaused: false,
            AutoResumeOnUsageReset: true,
            DefaultCooldownMinutes: 0,
            InstallDotnet: false,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false,
            ImageBuildStatus: ImageBuildStatus.Idle,
            LastImageBuildError: null,
            HasUsableImage: false,
            NextRetryAt: null,
            Attempt: 0);

        public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult((GlobalSettingsSummary?)DefaultSummary);

        public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(1);

        public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(60);

        public Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<(string?, string?)>((null, null));

        public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
            => Task.FromResult(new DispatchPauseState(null, false, true));

        public Task<int> GetDefaultCooldownMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(
            CancellationToken cancellationToken)
            => Task.FromResult((IReadOnlyDictionary<string, string>)new Dictionary<string, string>());
    }

    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Foundry.WebApi";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
    }
}
