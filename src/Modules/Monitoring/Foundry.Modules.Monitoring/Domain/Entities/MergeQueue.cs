using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Domain.Entities;

/// <summary>
/// One repository's <see cref="PendingMerge"/> rows and the rules that keep them in step with its merge-pending
/// issues, including the catch-up queue (ADR 0111): a merge refused because the pull request is behind joins the
/// queue, and only its head rebases, one catch-up at a time. It is not persisted itself: it has no state beyond its
/// rows, so the merge step builds it from the rows it loads and adds or removes the rows it returns.
/// </summary>
internal sealed class MergeQueue
{
    /// <summary>Catch-up rounds one queue entry may start; the next refusal as behind resolves <c>fell_behind</c>.</summary>
    public const int MaxCatchUpRounds = 3;

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
    /// The numbers of the issues whose rows are kept: every merge-pending issue, and every issue rebasing or waiting
    /// on checks for a catch-up, whose queue entry keeps its place through them.
    /// </summary>
    public static IReadOnlySet<int> HeldIssueNumbers(
        IEnumerable<MergePendingIssueInfo> mergePendingIssues,
        IEnumerable<MergeQueueIssueInfo> queueIssues) =>
        mergePendingIssues.Select(i => i.IssueNumber)
            .Concat(queueIssues.Where(i => i.IsCatchingUp).Select(i => i.IssueNumber))
            .ToHashSet();

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
    /// Drops and returns the rows of issues that left the merge path: merged, sent to review, queued for a revision,
    /// closed or untracked. <paramref name="heldIssueNumbers"/> must be every held issue of the repository
    /// (<see cref="HeldIssueNumbers"/>).
    /// </summary>
    public IReadOnlyList<PendingMerge> Forget(IReadOnlySet<int> heldIssueNumbers)
    {
        List<PendingMerge> forgotten = _rows.Values.Where(m => !heldIssueNumbers.Contains(m.IssueNumber)).ToList();
        foreach (PendingMerge merge in forgotten)
        {
            _rows.Remove(merge.IssueNumber);
        }

        return forgotten;
    }

    /// <summary>
    /// The merge of <paramref name="merge"/> was refused because its pull request is behind the default branch: the
    /// entry waits in the queue for a catch-up, unless it has already used <see cref="MaxCatchUpRounds"/>.
    /// </summary>
    public static BehindRefusal RefuseBehind(PendingMerge merge, DateTimeOffset now)
    {
        if (merge.CatchUpRounds >= MaxCatchUpRounds)
        {
            return BehindRefusal.FellBehind;
        }

        merge.AwaitCatchUp(now);
        return BehindRefusal.Queued;
    }

    /// <summary>
    /// The queue head: the queued row of a held issue with the earliest <see cref="PendingMerge.QueuedAt"/>, then the
    /// lowest issue number. Only the head starts a catch-up. <see langword="null"/> when nothing is queued.
    /// </summary>
    public PendingMerge? Head(IReadOnlySet<int> heldIssueNumbers) =>
        _rows.Values
            .Where(m => m.IsQueued && heldIssueNumbers.Contains(m.IssueNumber))
            .OrderBy(m => m.QueuedAt)
            .ThenBy(m => m.IssueNumber)
            .FirstOrDefault();

    /// <summary>
    /// The issues the head waits for when their pull request is already up to date with the default branch: another
    /// issue in checks-pending or merge-pending, not catching up and not itself queued, whose merge is still ahead
    /// (no outcome resolved for its episode) and that no person's feedback sends to review instead.
    /// </summary>
    public IReadOnlyList<MergeQueueIssueInfo> AwaitableIssues(
        PendingMerge head,
        IEnumerable<MergeQueueIssueInfo> queueIssues) =>
        queueIssues
            .Where(i => i.IssueNumber != head.IssueNumber && !i.IsCatchingUp && !i.HasPersonFeedback)
            .Where(i => !_rows.TryGetValue(i.IssueNumber, out PendingMerge? row) || (!row.IsQueued && !row.IsResolved))
            .OrderBy(i => i.IssueNumber)
            .ToList();
}

/// <summary>What <see cref="MergeQueue.RefuseBehind"/> made of a merge refused because its pull request is behind.</summary>
internal enum BehindRefusal
{
    /// <summary>The entry waits in the queue for a catch-up.</summary>
    Queued,

    /// <summary>The entry used every catch-up round; the merge resolves <c>skipped(fell_behind)</c>.</summary>
    FellBehind,
}
