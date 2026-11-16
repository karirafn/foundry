using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

namespace Foundry.Testing;

/// <summary>A catch-up queue with no entries: every issue reads as having no queue row.</summary>
public sealed class NullMergeQueueQueries : IMergeQueueQueries
{
    public Task<MergeQueueEntry?> GetQueueEntryAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        CancellationToken cancellationToken) =>
        Task.FromResult<MergeQueueEntry?>(null);
}
