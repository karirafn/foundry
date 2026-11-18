using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;

namespace Foundry.UnitTests.Modules.Monitoring.Features.AccountTokenHealth;

/// <summary>Returns whatever expiry counts the test last set, and records the instant it was asked for.</summary>
internal sealed class ScriptedExpiryQueries : IAccountTokenHealthQueries
{
    public AccountTokenExpiryCounts Counts { get; set; } = new(ExpiringSoon: 0, Expired: 0);

    public List<DateTimeOffset> AskedAt { get; } = [];

    public Task<int> CountInvalidAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("The expiry banner never reads the invalid count.");

    public Task<AccountTokenExpiryCounts> CountExpiryAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        AskedAt.Add(now);
        return Task.FromResult(Counts);
    }
}

/// <summary>Captures notifications, or throws while <see cref="Failing"/> is set.</summary>
internal sealed class SwitchableBroadcaster : ISystemNotificationBroadcaster
{
    private readonly List<SystemNotification> _sent = [];

    public bool Failing { get; set; }

    public IReadOnlyList<SystemNotification> Sent => _sent;

    public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
    {
        if (Failing)
        {
            throw new InvalidOperationException("The hub is unavailable.");
        }

        _sent.Add(notification);
        return Task.CompletedTask;
    }
}
