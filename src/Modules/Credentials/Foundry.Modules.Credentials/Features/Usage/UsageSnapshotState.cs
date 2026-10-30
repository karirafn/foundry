namespace Foundry.Modules.Credentials.Features.Usage;

internal interface IUsageSnapshotState
{
    UsageSnapshot? Current { get; }
}

internal interface IUsageSnapshotStateMutator
{
    void SetSnapshot(UsageSnapshot snapshot);
    void MarkRefreshFailed(DateTimeOffset failedAt);
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

    public void MarkRefreshFailed(DateTimeOffset failedAt)
    {
        lock (_lock)
        {
            if (_current is null)
            {
                return;
            }

            _current = _current with { LastRefreshFailedAt = failedAt };
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
