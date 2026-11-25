using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>
/// Requests a scheduled rebuild once the last successful build is <see cref="RebuildAge"/> old, so a new SDK release
/// reaches workers without a redeploy (ADR 0125). The age comes from the persisted last build time, so a host restart
/// neither skips nor doubles a rebuild.
/// </summary>
internal sealed class WorkerImageRebuildScheduler(
    IWorkerImageRebuildQueue rebuildQueue,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<WorkerImageRebuildScheduler> logger) : PeriodicBackgroundService(logger)
{
    internal static readonly TimeSpan RebuildAge = TimeSpan.FromHours(24);

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override TimeSpan TickInterval => Interval;

    protected override Task TickAsync(CancellationToken cancellationToken) => ExecuteTickAsync(cancellationToken);

    internal async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IGlobalSettingsQueries settingsQueries = scope.ServiceProvider.GetRequiredService<IGlobalSettingsQueries>();
        GlobalSettingsSummary? settings = await settingsQueries.GetSettingsAsync(cancellationToken);

        if (settings is null || !IsRebuildDue(settings, timeProvider.GetUtcNow()))
        {
            return;
        }

        logger.LogInformation(
            "Requesting the scheduled worker image rebuild; the last successful build was at {LastImageBuiltAt}.",
            settings.LastImageBuiltAt);

        rebuildQueue.TryEnqueue(ImageRebuildOrigin.Scheduled);
    }

    // Without a recorded build the startup rebuild applies. A running build or a pending retry already covers the
    // request: the retry re-enqueues on its own, and requesting now would start a second build alongside it.
    private static bool IsRebuildDue(GlobalSettingsSummary settings, DateTimeOffset now) =>
        settings.BuildEnabled
        && settings.LastImageBuiltAt is DateTimeOffset lastBuiltAt
        && now - lastBuiltAt >= RebuildAge
        && settings.ImageBuildStatus is ImageBuildStatus.Idle
        && settings.NextRetryAt is null;
}
