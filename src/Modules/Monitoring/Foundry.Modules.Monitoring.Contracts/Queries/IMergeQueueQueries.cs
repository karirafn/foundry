namespace Foundry.Modules.Monitoring.Contracts.Queries;

/// <summary>
/// Reads an issue's entry in its repository's catch-up queue (ADR 0111) from the merge step's persisted rows.
/// </summary>
public interface IMergeQueueQueries
{
    /// <summary>
    /// Returns the queue entry of <paramref name="issueNumber"/> in <paramref name="repositoryId"/>, or
    /// <see langword="null"/> when the merge step tracks no row for it.
    /// </summary>
    Task<MergeQueueEntry?> GetQueueEntryAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the issues of <paramref name="repositoryIds"/> whose queue entry waits for its next catch-up (its
    /// <see cref="MergeQueueEntry.Wait"/> is set), read for many issues at once as the board summaries need.
    /// </summary>
    Task<IReadOnlySet<(MonitoredRepositoryId RepositoryId, int IssueNumber)>> GetWaitingIssuesAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// An issue's merge progress as the catch-up queue sees it. <paramref name="CatchUpRounds"/> counts the catch-ups the
/// entry has started, out of <paramref name="CatchUpRoundLimit"/>. <paramref name="Wait"/> is set only while the
/// entry waits for its next catch-up.
/// </summary>
public sealed record MergeQueueEntry(int CatchUpRounds, int CatchUpRoundLimit, MergeQueueWait? Wait);

/// <summary>
/// A waiting entry's place: <paramref name="Position"/> is 1-based among the repository's queued entries, ordered as
/// the queue picks its head (earliest join, then lowest issue number), so 1 means nothing ahead is still waiting or
/// catching up. <paramref name="WaitingForIssueNumber"/> is the issue whose pull request is already up to date and
/// that the head waits for to merge first; <see langword="null"/> when it waits for nothing in particular.
/// </summary>
public sealed record MergeQueueWait(int Position, int? WaitingForIssueNumber);
