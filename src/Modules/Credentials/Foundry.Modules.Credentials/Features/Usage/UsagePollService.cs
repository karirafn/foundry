using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Periodic background service that polls usage on a 5-minute interval and once at startup.
/// The coordinator itself gates on <see cref="ClaudeAccount.CanReportUsage"/> and the login
/// session state — this service simply calls it each tick.
/// </summary>
internal sealed class UsagePollService : PeriodicBackgroundService
{
    internal static readonly TimeSpan DefaultTickInterval = TimeSpan.FromMinutes(5);

    private readonly IUsagePollCoordinator _coordinator;

    // Explicit constructor required — PeriodicBackgroundService has a protected constructor,
    // so primary constructors are not available here.
    public UsagePollService(
        IUsagePollCoordinator coordinator,
        ILogger<UsagePollService> logger) : base(logger)
    {
        _coordinator = coordinator;
    }

    protected override TimeSpan TickInterval => DefaultTickInterval;

    protected override string ServiceName => nameof(UsagePollService);

    protected override bool RunTickOnStartup => true;

    /// <summary>
    /// Exposes <see cref="TickAsync"/> for direct invocation in unit tests without
    /// spinning up the full <see cref="PeriodicBackgroundService.ExecuteAsync"/> loop.
    /// </summary>
    internal Task TickForTest(CancellationToken cancellationToken) => TickAsync(cancellationToken);

    /// <summary>
    /// Exposes <see cref="RunTickOnStartup"/> for unit test assertion.
    /// </summary>
    internal bool RunTickOnStartupForTest => RunTickOnStartup;

    protected override Task TickAsync(CancellationToken cancellationToken)
        => _coordinator.PollAsync(cancellationToken);
}
