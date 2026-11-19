using System.Collections.Concurrent;

using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Testing;

public sealed class RecordingRepositoryWebhookBroadcaster : IRepositoryWebhookBroadcaster
{
    private readonly ConcurrentQueue<MonitoredRepositoryId> _changed = new();
    private int _allChangedCount;

    public IReadOnlyList<MonitoredRepositoryId> Changed => [.. _changed];

    public int AllChangedCount => _allChangedCount;

    public Exception? ThrowOnSend { get; set; }

    public Task RepositoryWebhookChangedAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken)
    {
        if (ThrowOnSend is { } exception)
        {
            return Task.FromException(exception);
        }

        _changed.Enqueue(repositoryId);
        return Task.CompletedTask;
    }

    public Task RepositoryWebhooksChangedAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _allChangedCount);
        return Task.CompletedTask;
    }
}
