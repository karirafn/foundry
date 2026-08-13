using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.CreditProbe;

/// <summary>
/// Periodic background service that fires the credit probe whenever the account spend state is
/// <see cref="SpendState.Blocked"/> and the scheduled probe time has elapsed.
/// Runs every 30 seconds.
/// </summary>
internal sealed class CreditProbeService : PeriodicBackgroundService
{
    internal static readonly TimeSpan DefaultTickInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICreditProbeCoordinator _coordinator;
    private readonly ILogger<CreditProbeService> _log;
    private readonly DateTimeOffset? _nowOverride;

    // Explicit constructor required — PeriodicBackgroundService has a protected constructor,
    // so primary constructors are not available here.
    public CreditProbeService(
        IServiceScopeFactory scopeFactory,
        ICreditProbeCoordinator coordinator,
        ILogger<CreditProbeService> logger,
        DateTimeOffset? nowOverride = null) : base(logger)
    {
        _scopeFactory = scopeFactory;
        _coordinator = coordinator;
        _log = logger;
        _nowOverride = nowOverride;
    }

    protected override TimeSpan TickInterval => DefaultTickInterval;

    protected override string ServiceName => nameof(CreditProbeService);

    /// <summary>
    /// Exposes <see cref="TickAsync"/> for direct invocation in unit tests without
    /// spinning up the full <see cref="PeriodicBackgroundService.ExecuteAsync"/> loop.
    /// </summary>
    internal Task TickForTest(CancellationToken cancellationToken) => TickAsync(cancellationToken);

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();

        ClaudeAccount? account = await db.Set<ClaudeAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return;
        }

        if (account.SpendState is not SpendState.Blocked blocked)
        {
            return;
        }

        DateTimeOffset now = _nowOverride ?? DateTimeOffset.UtcNow;

        if (blocked.NextProbeAt > now)
        {
            return;
        }

        _log.LogDebug(
            "Credit probe due (NextProbeAt={NextProbeAt}). Invoking coordinator.",
            blocked.NextProbeAt);

        await _coordinator.TryRunProbeAsync(cancellationToken);
    }
}
