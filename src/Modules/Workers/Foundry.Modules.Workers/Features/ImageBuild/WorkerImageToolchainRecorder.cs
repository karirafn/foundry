using System.Threading.Channels;

using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>Told when the current worker image changes, so its toolchain gets recorded.</summary>
internal interface IWorkerImageToolchainRecorder
{
    /// <summary>
    /// The role catalog now names a different image: record its toolchain if it has none, restarting the backoff.
    /// </summary>
    void ImageChanged();
}

/// <summary>
/// Keeps the current worker image's toolchain recorded (ADR 0109). The current image is the role catalog's
/// <see cref="WorkerImageProvenance.ImageId"/>. Given it has no stored list, the image is probed at startup and after
/// every build or inspect; a failed probe is retried with backoff (1 minute, doubling, capped at 30) until a list is
/// stored, so a failure never waits for the next build to clear.
/// </summary>
internal sealed class WorkerImageToolchainRecorder(
    IServiceScopeFactory scopeFactory,
    IWorkerImageToolchainProbe probe,
    IWorkerRoleCatalogStore roleCatalogStore,
    ISystemNotificationBroadcaster broadcaster,
    TimeProvider timeProvider,
    ILogger<WorkerImageToolchainRecorder> logger) : BackgroundService, IWorkerImageToolchainRecorder
{
    internal static readonly ImageBuildRetryPolicy ProductionRetryPolicy = new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));

    private readonly ImageBuildRetryPolicy _retryPolicy = ProductionRetryPolicy;

    // One pending signal is enough: the loop always re-reads the catalog's current image.
    private readonly Channel<bool> _imageChanged = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>
    /// Test-only constructor overload: injects a fast-firing retry policy. DI uses the primary constructor.
    /// </summary>
    internal WorkerImageToolchainRecorder(
        IServiceScopeFactory scopeFactory,
        IWorkerImageToolchainProbe probe,
        IWorkerRoleCatalogStore roleCatalogStore,
        ISystemNotificationBroadcaster broadcaster,
        TimeProvider timeProvider,
        ILogger<WorkerImageToolchainRecorder> logger,
        ImageBuildRetryPolicy retryPolicy)
        : this(scopeFactory, probe, roleCatalogStore, broadcaster, timeProvider, logger)
    {
        _retryPolicy = retryPolicy;
    }

    public void ImageChanged() => _imageChanged.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int failedAttempts = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            string? imageId = roleCatalogStore.Current?.Image.ImageId;
            bool recorded = imageId is null || await TryRecordAsync(imageId, stoppingToken);

            // An unknown image ID cannot be probed; the next build or inspect that learns one signals again.
            TimeSpan? retryAfter = recorded ? null : _retryPolicy.ComputeBackoff(++failedAttempts);

            if (await WaitForImageChangeAsync(retryAfter, stoppingToken))
            {
                failedAttempts = 0;
            }
        }
    }

    /// <summary>Whether the image's toolchain is stored, probing and storing it first when it is not.</summary>
    internal async Task<bool> TryRecordAsync(string imageId, CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

            if (await dbContext.Set<WorkerImageToolchain>().AnyAsync(t => t.ImageId == imageId, cancellationToken))
            {
                return true;
            }

            Toolchain? toolchain = await probe.ProbeAsync(imageId, cancellationToken);

            if (toolchain is null)
            {
                return false;
            }

            dbContext.Set<WorkerImageToolchain>().Add(
                WorkerImageToolchain.Create(imageId, toolchain, timeProvider.GetUtcNow()));
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Recorded the toolchain of worker image {ImageId}: {Tools}.",
                imageId,
                string.Join(", ", toolchain.Entries.Select(e => $"{e.Name} {e.Version}")));

            await NotifyStoredAsync(imageId, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A failed record is retried with backoff; it must never stop the loop or the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                "Could not record the toolchain of worker image {ImageId}: {Error}",
                imageId,
                SecretRedactor.Redact(ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Sends the image-build notification so an open Settings page reloads and shows the stored list. The list is
    /// already stored, so a failed send is only logged: it must not make the loop probe the image again.
    /// </summary>
    private async Task NotifyStoredAsync(string imageId, CancellationToken cancellationToken)
    {
        try
        {
            await broadcaster.SendAsync(
                new SystemNotification(WorkerImageRebuildService.ImageBuildCategory, IsActive: false, Message: string.Empty),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A failed broadcast only delays the Settings refresh until the next reload.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                "Recorded the toolchain of worker image {ImageId} but could not notify the dashboard: {Error}",
                imageId,
                SecretRedactor.Redact(ex.Message));
        }
    }

    /// <returns><see langword="true"/> when the image changed; <see langword="false"/> when the retry delay elapsed.</returns>
    private async Task<bool> WaitForImageChangeAsync(TimeSpan? retryAfter, CancellationToken stoppingToken)
    {
        using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using CancellationTokenSource? delay = retryAfter is { } after ? new CancellationTokenSource(after, timeProvider) : null;
        using CancellationTokenRegistration onDelay = delay?.Token.Register(wait.Cancel) ?? default;

        try
        {
            await _imageChanged.Reader.ReadAsync(wait.Token);
            return true;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
