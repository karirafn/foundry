using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundry.Shared.Infrastructure;

public abstract class PeriodicBackgroundService : BackgroundService
{
    private readonly ILogger _logger;

    protected PeriodicBackgroundService(ILogger logger)
    {
        _logger = logger;
    }

    protected abstract TimeSpan TickInterval { get; }

    protected abstract Task TickAsync(CancellationToken cancellationToken);

    protected virtual string ServiceName => GetType().Name;

    /// <summary>
    /// When <see langword="true"/>, <see cref="ExecuteAsync"/> runs one guarded tick
    /// immediately before the first <see cref="PeriodicTimer"/> interval.
    /// Defaults to <see langword="false"/> so existing subclasses are unaffected.
    /// </summary>
    protected virtual bool RunTickOnStartup => false;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (RunTickOnStartup)
        {
            bool cancelled = await RunGuardedTickAsync(stoppingToken);

            if (cancelled)
            {
                return;
            }
        }

        using PeriodicTimer timer = new(TickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                bool cancelled = await RunGuardedTickAsync(stoppingToken);

                if (cancelled)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Clean shutdown — WaitForNextTickAsync threw on cancellation; not a failure.
        }
    }

    /// <summary>
    /// Runs <see cref="TickAsync"/> with exception handling identical to the
    /// in-loop tick. Returns <see langword="true"/> when the stopping token was
    /// cancelled mid-tick and the caller should exit immediately.
    /// </summary>
    private async Task<bool> RunGuardedTickAsync(CancellationToken stoppingToken)
    {
        try
        {
            await TickAsync(stoppingToken);
            return false;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Clean shutdown — stopping token tripped mid-tick; not a failure.
            return true;
        }
#pragma warning disable CA1031 // A tick failure must not crash the background service or the host; the error log surfaces the issue without interrupting the loop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Unhandled exception in {ServiceName} tick.", ServiceName);
            return false;
        }
    }
}
