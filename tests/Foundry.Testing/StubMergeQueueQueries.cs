using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

namespace Foundry.Testing;

/// <summary>
/// A catch-up queue whose waiting entries are set by the test, recording each batch read of them. No issue has a
/// single queue entry.
/// </summary>
public sealed class StubMergeQueueQueries : IMergeQueueQueries
{
    private readonly List<IReadOnlySet<MonitoredRepositoryId>> _waitingReads = [];

    public HashSet<(MonitoredRepositoryId RepositoryId, int IssueNumber)> Waiting { get; } = [];

    public IReadOnlyList<IReadOnlySet<MonitoredRepositoryId>> WaitingReads => _waitingReads;

    public Task<MergeQueueEntry?> GetQueueEntryAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        CancellationToken cancellationToken) =>
        Task.FromResult<MergeQueueEntry?>(null);

    public Task<IReadOnlySet<(MonitoredRepositoryId RepositoryId, int IssueNumber)>> GetWaitingIssuesAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken)
    {
        _waitingReads.Add(repositoryIds.ToHashSet());
        return Task.FromResult<IReadOnlySet<(MonitoredRepositoryId RepositoryId, int IssueNumber)>>(
            Waiting.Where(w => repositoryIds.Contains(w.RepositoryId)).ToHashSet());
    }
}
