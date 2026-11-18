using System.Reflection;

using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.AccountTokenHealth;

/// <summary>
/// Recounts the account-token-expiry banner every hour, catching what no account change announces: an
/// account crossing into the warning window or past its expiry, and expiries filled in by the backfill.
/// It broadcasts only when the counts differ from the last ones it sent; account changes broadcast on
/// their own through <see cref="AccountTokenExpiryBroadcastHandler"/>.
/// </summary>
internal sealed class AccountTokenExpiryAlertService : PeriodicBackgroundService
{
    private const string DocGenerationEntryAssemblyName = "GetDocument.Insider";

    private static readonly TimeSpan RecountInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;

    // In memory only: after a restart the startup tick broadcasts once, which the dashboard applies idempotently.
    private AccountTokenExpiryCounts? _lastSent;

    public AccountTokenExpiryAlertService(
        IServiceScopeFactory scopeFactory,
        ILogger<AccountTokenExpiryAlertService> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    protected override TimeSpan TickInterval => RecountInterval;

    protected override bool RunTickOnStartup => true;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // The build-time OpenAPI doc generation tool runs the app with no database to read accounts from.
        Assembly.GetEntryAssembly()?.GetName().Name == DocGenerationEntryAssemblyName
            ? Task.CompletedTask
            : base.ExecuteAsync(stoppingToken);

    protected override Task TickAsync(CancellationToken cancellationToken) => RecountAsync(cancellationToken);

    internal async Task RecountAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        AccountTokenExpiryBroadcaster broadcaster = scope.ServiceProvider.GetRequiredService<AccountTokenExpiryBroadcaster>();

        AccountTokenExpiryCounts counts = await broadcaster.CountAsync(cancellationToken);

        if (counts == _lastSent)
        {
            return;
        }

        // A failed send leaves the last-sent counts as they were, so the next tick tries again.
        if (await broadcaster.SendAsync(counts, cancellationToken))
        {
            _lastSent = counts;
        }
    }
}
