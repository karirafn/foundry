using System.Collections.Concurrent;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.HostPauses;

/// <summary>
/// The host-pause exit: probes every paused provider host whose next probe is due, and keeps doing so on the pause's
/// backoff until a probe reaches the provider. It runs whatever the global dispatch pause, so a host's state stays
/// accurate for when dispatch resumes.
/// </summary>
internal sealed class HostProbeService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<HostProbeService> logger) : PeriodicBackgroundService(logger)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    // A probe can outlast a tick (up to three containers); a host already being probed is skipped, never probed twice.
    private readonly ConcurrentDictionary<string, byte> _probing = new(StringComparer.OrdinalIgnoreCase);

    protected override TimeSpan TickInterval => Interval;

    protected override Task TickAsync(CancellationToken cancellationToken) => ExecuteTickAsync(cancellationToken);

    internal async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PausedHost> dueHosts = await ReadDueHostsAsync(cancellationToken);

        foreach (PausedHost paused in dueHosts)
        {
            if (!_probing.TryAdd(paused.Host, 0))
            {
                logger.LogDebug("A probe of {Host} is already running; skipping it this tick.", paused.Host);
                continue;
            }

            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                HostProber prober = scope.ServiceProvider.GetRequiredService<HostProber>();
                await prober.ProbeAsync(paused.Host, cancellationToken);
            }
            finally
            {
                _probing.TryRemove(paused.Host, out _);
            }
        }
    }

    private async Task<IReadOnlyList<PausedHost>> ReadDueHostsAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IGlobalSettingsQueries settingsQueries = scope.ServiceProvider.GetRequiredService<IGlobalSettingsQueries>();
        DispatchPauseState pauseState = await settingsQueries.GetDispatchPauseStateAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        return [.. pauseState.PausedHosts.Where(paused => paused.NextProbeAt <= now)];
    }
}
