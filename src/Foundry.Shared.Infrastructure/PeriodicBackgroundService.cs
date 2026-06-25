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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Clean shutdown — stopping token tripped mid-tick; not a failure.
                    return;
                }
#pragma warning disable CA1031 // A tick failure must not crash the background service or the host; the error log surfaces the issue without interrupting the loop.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    _logger.LogError(ex, "Unhandled exception in {ServiceName} tick.", ServiceName);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Clean shutdown — WaitForNextTickAsync threw on cancellation; not a failure.
        }
    }
}
