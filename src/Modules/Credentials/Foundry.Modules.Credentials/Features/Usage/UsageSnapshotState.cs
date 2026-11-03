namespace Foundry.Modules.Credentials.Features.Usage;

internal interface IUsageSnapshotState
{
    UsageSnapshot? Current { get; }
}

internal interface IUsageSnapshotStateMutator
{
    void SetSnapshot(UsageSnapshot snapshot);
    /// <summary>
    /// Records a failed poll. Retains the last good snapshot when one exists; otherwise stores an
    /// unavailable snapshot for <paramref name="accountEmail"/> so the failure is observable.
    /// </summary>
    void MarkRefreshFailed(string accountEmail, DateTimeOffset failedAt);
    void Clear();
}

internal sealed class UsageSnapshotState : IUsageSnapshotState, IUsageSnapshotStateMutator
{
    private readonly Lock _lock = new();
    private UsageSnapshot? _current;

    UsageSnapshot? IUsageSnapshotState.Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public void SetSnapshot(UsageSnapshot snapshot)
    {
        lock (_lock)
        {
            _current = snapshot with { LastRefreshFailedAt = null };
        }
    }

    public void MarkRefreshFailed(string accountEmail, DateTimeOffset failedAt)
    {
        lock (_lock)
        {
            _current = _current is null
                ? UsageSnapshot.Unavailable(accountEmail, failedAt)
                : _current with { LastRefreshFailedAt = failedAt };
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _current = null;
        }
    }
}
