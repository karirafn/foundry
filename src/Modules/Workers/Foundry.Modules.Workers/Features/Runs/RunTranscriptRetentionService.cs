using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Runs;

/// <summary>
/// Deletes Run Transcripts older than <see cref="RetentionPeriod"/>, measured from capture, in
/// batches of <see cref="DeleteBatchSize"/>. The run row, its Container Output and its summary stay.
/// </summary>
internal sealed class RunTranscriptRetentionService : PeriodicBackgroundService
{
    internal const int DeleteBatchSize = 200;

    internal static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);

    internal static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RunTranscriptRetentionService> _log;
    private DateTimeOffset? _lastCompletedSweepAt;

    // Explicit constructor required — PeriodicBackgroundService has a protected constructor,
    // so primary constructors are not available here.
    public RunTranscriptRetentionService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<RunTranscriptRetentionService> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _log = logger;
    }

    protected override TimeSpan TickInterval => Interval;

    protected override string ServiceName => nameof(RunTranscriptRetentionService);

    /// <summary>
    /// Exposes <see cref="TickAsync"/> for direct invocation in unit tests without
    /// spinning up the full <see cref="PeriodicBackgroundService.ExecuteAsync"/> loop.
    /// </summary>
    internal Task TickForTest(CancellationToken cancellationToken) => TickAsync(cancellationToken);

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (_lastCompletedSweepAt is { } last && now - last < SweepInterval)
        {
            return;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();

        DateTimeOffset cutoff = now - RetentionPeriod;

        // No OrderBy: every matching row is past retention, so any batch is correct.
        int deleted = await db.Set<WorkerRunTranscript>()
            .Where(t => t.CapturedAt < cutoff)
            .Take(DeleteBatchSize)
            .ExecuteDeleteAsync(cancellationToken);

        // A full batch may leave more expired rows, so the throttle stays open and the next tick
        // sweeps again; an underfull batch drained the backlog and restores the throttled cadence.
        if (deleted < DeleteBatchSize)
        {
            _lastCompletedSweepAt = now;
        }

        if (deleted > 0)
        {
            _log.LogInformation(
                "Run transcript retention sweep deleted {Count} transcript(s) captured before {Cutoff:O}.",
                deleted,
                cutoff);
        }
    }
}
