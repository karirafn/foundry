using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Pure ordering helper that produces the capacity-aware dispatch sequence from a
/// key-ordered set of queued issues.
/// </summary>
/// <remarks>
/// A stable multi-pass walk over <see cref="DispatchOrderKey"/> order:
/// <list type="bullet">
///   <item>Each pass traverses issues in key order.</item>
///   <item>An issue whose repository still has remaining headroom takes the next position and
///         decrements that repository's remaining headroom.</item>
///   <item>An issue whose repository is saturated (headroom = 0) defers to the following pass.</item>
///   <item>Passes repeat until all issues are placed or all remaining repositories are saturated.</item>
/// </list>
/// Headroom is supplied by the caller as a <see cref="DispatchHeadroom"/> value object. The
/// walk seeds a mutable working copy from the value object's snapshot so the walk never mutates
/// the immutable value object itself.
///
/// <para>
/// This walk is the single shared artifact consumed by both the dispatch selector and the
/// dashboard. Under normal conditions both consumers pass equivalent headroom. Under global
/// saturation the selector passes <see cref="DispatchHeadroom.ForClaim"/> headroom (as-is
/// capacity) and the dashboard passes <see cref="DispatchHeadroom.ForForecast"/> headroom
/// (one in-flight worker per repository assumed to finish), so the two consumers produce
/// different orderings from different <see cref="DispatchHeadroom"/> instances — the walk
/// itself is identical.
/// </para>
///
/// <para>
/// A sort-key shortcut (ADR 0074 rejected alternative) sees each issue independently and cannot
/// account for headroom consumed by an earlier issue of the same repository — it is correct at a
/// limit of 1 but wrong at limits of 2 or more.
/// </para>
/// </remarks>
internal static class CapacityAwareDispatchOrder
{
    /// <summary>
    /// Returns the capacity-aware dispatch sequence for the given issues.
    /// </summary>
    /// <param name="keyedIssues">
    /// Issues paired with their pre-computed <see cref="DispatchOrderKey"/>, already sorted
    /// or in any order — the walk re-sorts them internally.
    /// </param>
    /// <param name="headroom">
    /// Per-repository remaining dispatch slots, floored at 0.
    /// Repositories absent from the value object contribute no headroom.
    /// The walk seeds a fresh mutable copy from <see cref="DispatchHeadroom.Snapshot"/> so
    /// the immutable value object is never mutated — a single instance may be reused across
    /// multiple calls without carrying decremented state.
    /// </param>
    /// <returns>
    /// Issues in placement order. Issues whose repository is saturated everywhere are excluded.
    /// </returns>
    internal static IReadOnlyList<QueuedIssue> Order(
        IReadOnlyList<(QueuedIssue Issue, DispatchOrderKey Key)> keyedIssues,
        DispatchHeadroom headroom)
    {
        if (keyedIssues.Count == 0)
        {
            return [];
        }

        // Sort all issues by their dispatch-order key (ascending = dispatched first).
        List<(QueuedIssue Issue, DispatchOrderKey Key)> sorted = [..keyedIssues];
        sorted.Sort(static (a, b) => a.Key.CompareTo(b.Key));

        // Working copy of headroom — decremented as issues are placed.
        // Seeded from a fresh copy of the immutable snapshot so the value object is never mutated.
        Dictionary<MonitoredRepositoryId, int> remaining = new(headroom.Snapshot);

        List<QueuedIssue> placed = new(sorted.Count);
        List<(QueuedIssue Issue, DispatchOrderKey Key)> deferred = new(sorted.Count);

        List<(QueuedIssue Issue, DispatchOrderKey Key)> pending = sorted;

        while (pending.Count > 0)
        {
            deferred.Clear();

            foreach ((QueuedIssue issue, DispatchOrderKey key) in pending)
            {
                MonitoredRepositoryId repoId = issue.MonitoredRepositoryId;

                remaining.TryGetValue(repoId, out int slots);

                if (slots > 0)
                {
                    placed.Add(issue);
                    remaining[repoId] = slots - 1;
                }
                else
                {
                    deferred.Add((issue, key));
                }
            }

            // If no issue was placed this pass every remaining repository is saturated — stop.
            if (deferred.Count == pending.Count)
            {
                break;
            }

            // Swap lists for the next pass.
            (pending, deferred) = (deferred, pending);
        }

        return placed;
    }
}
