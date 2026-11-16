using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>
/// Repeats the <see cref="FoundryImageSweep"/> every hour, so an image a run's container still used at the last build
/// is removed once that container is gone, without waiting for the next build.
/// </summary>
internal sealed class FoundryImageSweepService(
    FoundryImageSweep imageSweep,
    ILogger<FoundryImageSweepService> logger) : PeriodicBackgroundService(logger)
{
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override TimeSpan TickInterval => Interval;

    protected override Task TickAsync(CancellationToken cancellationToken) => imageSweep.SweepAsync(cancellationToken);

    /// <summary>
    /// Exposes <see cref="TickAsync"/> for direct invocation in unit tests without
    /// spinning up the full <see cref="PeriodicBackgroundService.ExecuteAsync"/> loop.
    /// </summary>
    internal Task TickForTest(CancellationToken cancellationToken) => TickAsync(cancellationToken);
}
