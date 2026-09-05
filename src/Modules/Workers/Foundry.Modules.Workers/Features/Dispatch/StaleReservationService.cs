using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>
/// Periodic background service that sweeps <see cref="DispatchReservation"/> rows older
/// than <see cref="StaleReservationThreshold"/> and deletes them so dispatch is not
/// permanently deadlocked by crash-stranded or dead-lettered reservations.
/// <para>
/// Decision D4 (ADR 0069): this service is deliberately independent of
/// <see cref="StaleStartingRunService"/>. <c>StaleStartingRunService</c> returns early
/// on Docker failure — folding reservation release into it would disable release
/// exactly when an unreachable daemon is causing reservations to accumulate.
/// </para>
/// </summary>
internal sealed class StaleReservationService : PeriodicBackgroundService
{
    internal static readonly TimeSpan StaleReservationThreshold = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleReservationService> _log;

    // Explicit constructor required — PeriodicBackgroundService has a protected constructor,
    // so primary constructors are not available here.
    public StaleReservationService(
        IServiceScopeFactory scopeFactory,
        ILogger<StaleReservationService> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
        _log = logger;
    }

    protected override TimeSpan TickInterval => Interval;

    protected override string ServiceName => nameof(StaleReservationService);

    /// <summary>
    /// Exposes <see cref="TickAsync"/> for direct invocation in unit tests without
    /// spinning up the full <see cref="PeriodicBackgroundService.ExecuteAsync"/> loop.
    /// </summary>
    internal Task TickForTest(CancellationToken cancellationToken) => TickAsync(cancellationToken);

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();

        // Load all reservations for in-memory staleness filtering.
        // SQLite cannot translate DateTimeOffset comparisons reliably — mirror the
        // same pattern used by StaleStartingRunService (see StaleStartingRunService.cs:95-99).
        List<DispatchReservation> reservations = await db.Set<DispatchReservation>()
            .ToListAsync(cancellationToken);

        DateTimeOffset cutoff = DateTimeOffset.UtcNow - StaleReservationThreshold;

        List<DispatchReservation> stale = reservations
            .Where(r => r.ReservedAt < cutoff)
            .ToList();

        if (stale.Count == 0)
        {
            return;
        }

        db.Set<DispatchReservation>().RemoveRange(stale);
        await db.SaveChangesAsync(cancellationToken);

        _log.LogInformation(
            "Swept {Count} stale dispatch reservation(s) older than {ThresholdMinutes} minutes.",
            stale.Count,
            StaleReservationThreshold.TotalMinutes);
    }
}
