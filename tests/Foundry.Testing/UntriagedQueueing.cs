using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Testing;

/// <summary>
/// Queues or blocks a detected issue the way Skip triage does: no verdict recorded, its kind as the labels decided.
/// For tests about the queue and blockers rather than triage, which every new issue otherwise goes through first.
/// </summary>
public static class UntriagedQueueing
{
    public static FreshQueuedIssue Enqueue(this DetectedIssue detected) =>
        (FreshQueuedIssue)SkipTriage(detected);

    public static BlockedIssue Block(this DetectedIssue detected, IReadOnlyList<int> blockers)
    {
        ArgumentNullException.ThrowIfNull(detected);

        if (blockers.Count == 0)
        {
            throw new InvalidOperationException("Cannot block an issue without specifying at least one blocker.");
        }

        detected.SetBlockedBy(blockers);
        return (BlockedIssue)SkipTriage(detected);
    }

    private static Foundry.Modules.Issues.Domain.Entities.Issue SkipTriage(DetectedIssue detected) =>
        detected.QueueTriage(detected.DetectedAt)
            .Claim(WorkerRunId.New(), detected.DetectedAt)
            .MarkFailed("Skipped in test", FailureCategory.NonZeroExit, DateTimeOffset.UnixEpoch)
            .SkipTriage(detected.DetectedAt);
}
