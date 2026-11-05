using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.SessionCheck;

/// <summary>
/// Periodic background service that verifies the stored Claude OAuth session once at startup and
/// then on a fixed interval, so an expired login pauses dispatch before workers fail on it.
/// The coordinator gates on the account and login state; a failed tick is logged by the base class
/// and the next tick retries.
/// </summary>
internal sealed class OAuthSessionCheckService : PeriodicBackgroundService
{
    internal static readonly TimeSpan DefaultTickInterval = TimeSpan.FromMinutes(5);

    private readonly IOAuthSessionCheckCoordinator _coordinator;

    // Explicit constructor required — PeriodicBackgroundService has a protected constructor.
    public OAuthSessionCheckService(
        IOAuthSessionCheckCoordinator coordinator,
        ILogger<OAuthSessionCheckService> logger) : base(logger)
    {
        _coordinator = coordinator;
    }

    protected override TimeSpan TickInterval => DefaultTickInterval;

    protected override string ServiceName => nameof(OAuthSessionCheckService);

    protected override bool RunTickOnStartup => true;

    internal Task TickForTest(CancellationToken cancellationToken) => TickAsync(cancellationToken);

    internal bool RunTickOnStartupForTest => RunTickOnStartup;

    protected override Task TickAsync(CancellationToken cancellationToken)
        => _coordinator.CheckAsync(cancellationToken);
}
