using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Domain.Entities;

/// <summary>
/// One repository's <see cref="PendingMerge"/> rows and the rules that keep them in step with its merge-pending
/// issues (ADR 0111). It is not persisted itself: it has no state beyond its rows, so the merge step builds it from
/// the rows it loads and adds or removes the rows it returns.
/// </summary>
internal sealed class MergeQueue
{
    private readonly Dictionary<int, PendingMerge> _rows;

    private MergeQueue(MonitoredRepositoryId repositoryId, Dictionary<int, PendingMerge> rows)
    {
        RepositoryId = repositoryId;
        _rows = rows;
    }

    public MonitoredRepositoryId RepositoryId { get; }

    /// <summary>Builds the queue of <paramref name="repositoryId"/> from its persisted rows.</summary>
    /// <exception cref="ArgumentException">
    /// A row belongs to another repository, or two rows track the same issue.
    /// </exception>
    public static MergeQueue From(MonitoredRepositoryId repositoryId, IEnumerable<PendingMerge> rows)
    {
        Dictionary<int, PendingMerge> byIssue = [];
        foreach (PendingMerge row in rows)
        {
            if (row.MonitoredRepositoryId != repositoryId)
            {
                throw new ArgumentException(
                    $"Pending merge of issue #{row.IssueNumber} belongs to repository {row.MonitoredRepositoryId}, not {repositoryId}.",
                    nameof(rows));
            }

            if (!byIssue.TryAdd(row.IssueNumber, row))
            {
                throw new ArgumentException($"Issue #{row.IssueNumber} has more than one pending merge.", nameof(rows));
            }
        }

        return new MergeQueue(repositoryId, byIssue);
    }

    /// <summary>
    /// Returns the row of <paramref name="issue"/>'s current merge-pending episode: a new row on its first sighting,
    /// and the existing row otherwise, restarted when the issue has since entered merge-pending again.
    /// </summary>
    public TrackedMerge Track(MergePendingIssueInfo issue)
    {
        if (!_rows.TryGetValue(issue.IssueNumber, out PendingMerge? merge))
        {
            merge = PendingMerge.Start(RepositoryId, issue.IssueNumber, issue.EnteredMergePendingAt);
            _rows[issue.IssueNumber] = merge;
            return new TrackedMerge.Started(merge);
        }

        if (!merge.IsForEpisode(issue.EnteredMergePendingAt))
        {
            merge.Restart(issue.EnteredMergePendingAt);
        }

        return new TrackedMerge.Existing(merge);
    }

    /// <summary>
    /// Drops and returns the rows of issues that left merge-pending (resolved, closed or untracked).
    /// <paramref name="mergePendingIssues"/> must be every merge-pending issue of the repository.
    /// </summary>
    public IReadOnlyList<PendingMerge> Forget(IEnumerable<MergePendingIssueInfo> mergePendingIssues)
    {
        HashSet<int> pendingNumbers = mergePendingIssues.Select(i => i.IssueNumber).ToHashSet();
        List<PendingMerge> forgotten = _rows.Values.Where(m => !pendingNumbers.Contains(m.IssueNumber)).ToList();
        foreach (PendingMerge merge in forgotten)
        {
            _rows.Remove(merge.IssueNumber);
        }

        return forgotten;
    }
}
