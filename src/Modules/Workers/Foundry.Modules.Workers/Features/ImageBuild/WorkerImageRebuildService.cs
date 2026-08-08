using System.Formats.Tar;
using System.Text.Json;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Features.ImageBuild;

internal sealed class WorkerImageRebuildService(
    IWorkerImageRebuildQueue rebuildQueue,
    IServiceScopeFactory scopeFactory,
    IImageOperations imageOperations,
    IHostEnvironment hostEnvironment,
    IOptions<WorkerOptions> optionsAccessor,
    ISystemNotificationBroadcaster broadcaster,
    ILogger<WorkerImageRebuildService> logger) : BackgroundService, IHostedLifecycleService
{
    internal const string ImageBuildCategory = "image-build";
    internal const string BuildingStatus = "Building";
    internal const string FailedStatus = "Failed";
    internal const string BaseDockerfile = "Dockerfile.base";
    internal const string LoginDockerfile = "Dockerfile.login";

    // These constants are promoted to WorkerImageNames in Contracts so the Settings module
    // can compose the login command without a cross-module circular dependency.
    internal const string BaseImageTag = WorkerImageNames.BaseImageTag;
    internal const string LoginImageName = WorkerImageNames.LoginImageName;

    private const int LogTailLines = 200;

    private readonly WorkerOptions _options = optionsAccessor.Value;

    // Backoff state — all mutations guarded by _stateLock.
    private readonly Lock _stateLock = new();
    private int _attempt;
    private CancellationTokenSource? _pendingRetryCts;

    private bool _eventSubscribed;

    private void SubscribeToImmediateRebuild()
    {
        if (_eventSubscribed)
        {
            return;
        }

        rebuildQueue.ImmediateRebuildRequested += OnImmediateRebuildRequested;
        _eventSubscribed = true;
    }

    private void OnImmediateRebuildRequested()
    {
        CancellationTokenSource? ctsToCancel;
        lock (_stateLock)
        {
            _attempt = 0;
            ctsToCancel = _pendingRetryCts;
            _pendingRetryCts = null;
        }

        // Cancel and dispose outside the lock — cancellation callbacks must not re-acquire the lock.
        if (ctsToCancel is not null)
        {
#pragma warning disable CA1849 // CancelAsync is not available in sync event handler context.
            ctsToCancel.Cancel();
#pragma warning restore CA1849
            ctsToCancel.Dispose();
        }
    }

    public override void Dispose()
    {
        // Unsubscribe before disposal to avoid callbacks on a disposed instance.
        rebuildQueue.ImmediateRebuildRequested -= OnImmediateRebuildRequested;

        CancellationTokenSource? ctsToDispose;
        lock (_stateLock)
        {
            ctsToDispose = _pendingRetryCts;
            _pendingRetryCts = null;
        }

        ctsToDispose?.Dispose();

        base.Dispose();
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        SubscribeToImmediateRebuild();

        if (!_options.ImageBuild.Enabled)
        {
            logger.LogInformation("Startup image rebuild skipped: ImageBuild.Enabled is false.");
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            logger.LogWarning(
                "Startup image rebuild skipped: no GlobalSettings row exists.");
            return;
        }

        rebuildQueue.RequestImmediateRebuild();
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SubscribeToImmediateRebuild();

        await foreach (bool _ in rebuildQueue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessRebuildAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Clean shutdown — stopping token tripped mid-item; not a failure.
                return;
            }
#pragma warning disable CA1031 // An item failure must not crash the background service or the host; the error log surfaces the issue without interrupting the consumer loop.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Unhandled exception in {ServiceName} while processing image rebuild.", nameof(WorkerImageRebuildService));
            }
        }
    }

    internal async Task ProcessRebuildAsync(CancellationToken cancellationToken)
    {
        if (!_options.ImageBuild.Enabled)
        {
            logger.LogInformation("Image rebuild skipped: ImageBuild.Enabled is false.");
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            logger.LogWarning(
                "Worker image rebuild requested but no GlobalSettings row exists; skipping.");
            return;
        }

        await broadcaster.SendAsync(
            new SystemNotification(ImageBuildCategory, true, SerializeBuildingNotification()),
            cancellationToken);

        settings.BeginImageBuild();
        await dbContext.SaveChangesAsync(cancellationToken);

        string contextPath = ResolveContextPath(_options.ImageBuild.ContextPath);
        IReadOnlyDictionary<string, string> buildArgs = settings.WorkerImageConfiguration.ToBuildArgs();

        logger.LogInformation(
            "Building worker image '{Image}' from context '{ContextPath}'.",
            _options.Image,
            contextPath);

        string? errorTail = null;

        try
        {
            await using MemoryStream tarStream = new();
            await TarFile.CreateFromDirectoryAsync(
                contextPath,
                tarStream,
                includeBaseDirectory: false,
                cancellationToken);
            tarStream.Seek(0, SeekOrigin.Begin);

            // Build the shared base image first; the worker image depends on it.
            ImageBuildParameters baseParameters = new()
            {
                Tags = [BaseImageTag],
                BuildArgs = new Dictionary<string, string>(),
                Dockerfile = BaseDockerfile,
            };

            BuildProgress baseProgress = new(logger);

            await imageOperations.BuildImageFromDockerfileAsync(
                baseParameters,
                tarStream,
                authConfigs: null,
                headers: null,
                baseProgress,
                cancellationToken);

            if (baseProgress.HasError)
            {
                errorTail = TruncateTail(baseProgress.LastError);
            }
            else
            {
                // Base image succeeded — rewind tar stream and build the worker image.
                tarStream.Seek(0, SeekOrigin.Begin);

                Dictionary<string, string> workerBuildArgs = new(buildArgs)
                {
                    ["BASE_IMAGE"] = BaseImageTag,
                };

                ImageBuildParameters workerParameters = new()
                {
                    Tags = [_options.Image],
                    BuildArgs = workerBuildArgs,
                    Dockerfile = "Dockerfile",
                };

                BuildProgress workerProgress = new(logger);

                await imageOperations.BuildImageFromDockerfileAsync(
                    workerParameters,
                    tarStream,
                    authConfigs: null,
                    headers: null,
                    workerProgress,
                    cancellationToken);

                if (workerProgress.HasError)
                {
                    errorTail = TruncateTail(workerProgress.LastError);
                }
                else
                {
                    // Worker image succeeded — rewind tar stream and build the login image.
                    // The login image must exist for the guided `docker run` login command to work.
                    // A login-image build failure fails the whole rebuild so the command is never broken.
                    tarStream.Seek(0, SeekOrigin.Begin);

                    Dictionary<string, string> loginBuildArgs = new()
                    {
                        ["BASE_IMAGE"] = BaseImageTag,
                    };

                    ImageBuildParameters loginParameters = new()
                    {
                        Tags = [LoginImageName],
                        BuildArgs = loginBuildArgs,
                        Dockerfile = LoginDockerfile,
                    };

                    BuildProgress loginProgress = new(logger);

                    await imageOperations.BuildImageFromDockerfileAsync(
                        loginParameters,
                        tarStream,
                        authConfigs: null,
                        headers: null,
                        loginProgress,
                        cancellationToken);

                    if (loginProgress.HasError)
                    {
                        errorTail = TruncateTail(loginProgress.LastError);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Docker build failures must be surfaced as status notifications, not exceptions, to avoid crashing the BackgroundService consumer loop.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Worker image build failed with an unhandled exception.");
            errorTail = TruncateTail(SecretRedactor.Redact(ex.Message));
        }

        if (errorTail is null)
        {
            CancellationTokenSource? oldCts = SwapPendingRetryCts(replacement: null, resetAttempt: true);
            await CancelAndDisposeAsync(oldCts);

            settings.CompleteImageBuild();
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Worker image '{Image}' built successfully.", _options.Image);

            await broadcaster.SendAsync(
                new SystemNotification(ImageBuildCategory, false, string.Empty),
                cancellationToken);
        }
        else
        {
            CancellationTokenSource retryCts = new();
            CancellationTokenSource? oldCts = SwapPendingRetryCts(retryCts, resetAttempt: false);
            await CancelAndDisposeAsync(oldCts);

            int attempt;
            DateTimeOffset nextRetryAt;
            lock (_stateLock)
            {
                _attempt++;
                attempt = _attempt;
                nextRetryAt = DateTimeOffset.UtcNow + ComputeBackoff(attempt);
            }

            settings.FailImageBuild(errorTail, nextRetryAt, attempt);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogError(
                "Worker image '{Image}' build failed (attempt {Attempt}, next retry at {NextRetryAt}): {Error}",
                _options.Image,
                attempt,
                nextRetryAt,
                errorTail);

            await broadcaster.SendAsync(
                new SystemNotification(ImageBuildCategory, true, SerializeFailedNotification(errorTail, nextRetryAt, attempt)),
                cancellationToken);

            // Fire-and-forget delayed re-enqueue. Link both the service stopping token and the
            // per-retry CTS so that either a shutdown or a superseding RequestImmediateRebuild
            // can cancel the wait. The linked CTS created inside ScheduleDelayedRetryAsync is
            // disposed within the task itself after the delay completes or is cancelled.
#pragma warning disable CA2025 // The linked CancellationTokenSource is owned and disposed by ScheduleDelayedRetryAsync; the task disposes it internally before returning.
            _ = ScheduleDelayedRetryAsync(nextRetryAt - DateTimeOffset.UtcNow, retryCts, cancellationToken);
#pragma warning restore CA2025
        }
    }

    private CancellationTokenSource? SwapPendingRetryCts(CancellationTokenSource? replacement, bool resetAttempt)
    {
        lock (_stateLock)
        {
            if (resetAttempt)
            {
                _attempt = 0;
            }

            CancellationTokenSource? old = _pendingRetryCts;
            _pendingRetryCts = replacement;
            return old;
        }
    }

    private static async Task CancelAndDisposeAsync(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        await cts.CancelAsync();
        cts.Dispose();
    }

    private async Task ScheduleDelayedRetryAsync(
        TimeSpan delay,
        CancellationTokenSource retryCts,
        CancellationToken stoppingToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            retryCts.Token,
            stoppingToken);

        try
        {
            await Task.Delay(delay, linked.Token);
            rebuildQueue.TryEnqueue();
        }
        catch (OperationCanceledException)
        {
            // Either superseded by RequestImmediateRebuild or the service is stopping — both are intentional.
        }
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        // Exponential backoff: initialBackoff * 2^(attempt-1), capped at maxBackoff.
        // Use double arithmetic to avoid overflow on large attempt counts.
        double multiplier = Math.Pow(2, attempt - 1);
        double seconds = _options.ImageBuild.InitialBackoff.TotalSeconds * multiplier;
        TimeSpan uncapped = TimeSpan.FromSeconds(seconds);
        return uncapped < _options.ImageBuild.MaxBackoff ? uncapped : _options.ImageBuild.MaxBackoff;
    }

    private static string SerializeBuildingNotification()
        => JsonSerializer.Serialize(new ImageBuildStatusNotification(BuildingStatus, null, null, 0));

    private static string SerializeFailedNotification(string? logTail, DateTimeOffset? nextRetryAt, int attempt)
        => JsonSerializer.Serialize(new ImageBuildStatusNotification(FailedStatus, logTail, nextRetryAt, attempt));

    private string ResolveContextPath(string configuredContextPath)
    {
        // ContentRootPath is src/Foundry.WebApi/ — solution root is two directories up.
        string solutionRoot = Path.GetFullPath(
            Path.Combine(hostEnvironment.ContentRootPath, "..", ".."));

        return Path.GetFullPath(Path.Combine(solutionRoot, configuredContextPath));
    }

    private static string TruncateTail(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length > LogTailLines * 120
            ? text[^(LogTailLines * 120)..]
            : text;
    }

    private sealed class BuildProgress(ILogger logger) : IProgress<JSONMessage>
    {
        public bool HasError { get; private set; }
        public string? LastError { get; private set; }

        public void Report(JSONMessage message)
        {
            if (message.Error is not null)
            {
                HasError = true;
                LastError = message.Error.Message;
                logger.LogError("Docker build error: {Error}", message.Error.Message);
                return;
            }

            string? text = message.Stream?.TrimEnd('\n', '\r');
            if (!string.IsNullOrWhiteSpace(text))
            {
                logger.LogDebug("Docker build: {Message}", text);
            }
            else if (!string.IsNullOrWhiteSpace(message.Status))
            {
                logger.LogDebug("Docker build: {Status}", message.Status);
            }
        }
    }
}
