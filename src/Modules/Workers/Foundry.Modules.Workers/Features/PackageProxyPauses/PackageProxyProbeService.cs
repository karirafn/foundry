using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.PackageProxyPauses;

/// <summary>
/// The package-proxy pause's exit: probes the proxy once the pause's next probe is due, and keeps doing so on the
/// pause's backoff until a probe reaches it. It runs whatever the global dispatch pause, so the pause stays accurate
/// for when dispatch resumes.
/// </summary>
internal sealed class PackageProxyProbeService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PackageProxyProbeService> logger) : PeriodicBackgroundService(logger)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    // A probe can outlast a tick; one already running is never joined by a second.
    private int _probing;

    protected override TimeSpan TickInterval => Interval;

    protected override Task TickAsync(CancellationToken cancellationToken) => ExecuteTickAsync(cancellationToken);

    internal async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        if (!await IsProbeDueAsync(cancellationToken))
        {
            return;
        }

        if (Interlocked.Exchange(ref _probing, 1) == 1)
        {
            logger.LogDebug("A probe of the package proxy is already running; skipping it this tick.");
            return;
        }

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            PackageProxyProber prober = scope.ServiceProvider.GetRequiredService<PackageProxyProber>();
            await prober.ProbeAsync(cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _probing, 0);
        }
    }

    private async Task<bool> IsProbeDueAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDispatchPauseQueries dispatchPauseQueries = scope.ServiceProvider.GetRequiredService<IDispatchPauseQueries>();
        DispatchPauseState pauseState = await dispatchPauseQueries.GetDispatchPauseStateAsync(cancellationToken);

        return pauseState.PackageProxyPause is { } pause && pause.NextProbeAt <= timeProvider.GetUtcNow();
    }
}
