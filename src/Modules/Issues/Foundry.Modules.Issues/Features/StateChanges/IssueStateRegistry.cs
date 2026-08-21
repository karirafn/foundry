using System.Collections.Frozen;

using Foundry.Modules.Issues.Domain.Entities.States;

namespace Foundry.Modules.Issues.Features.StateChanges;

/// <summary>
/// Single source of truth for the issue lifecycle partition.
/// Maps wire names (EF discriminator strings) to their lifecycle group
/// and to the domain entity type used when building EF query predicates.
/// </summary>
internal static class IssueStateRegistry
{
    private static readonly FrozenDictionary<string, Type> ActiveMap = new Dictionary<string, Type>
    {
        ["detected"] = typeof(DetectedIssue),
        ["queued"] = typeof(FreshQueuedIssue),
        ["blocked"] = typeof(BlockedIssue),
        ["in_progress"] = typeof(InProgressIssue),
        ["review"] = typeof(ReviewIssue),
        ["unchanged"] = typeof(UnchangedIssue),
        ["failed"] = typeof(FailedIssue),
        ["continuable_failed"] = typeof(ContinuableFailedIssue),
        ["continuation_queued"] = typeof(ContinuationQueuedIssue),
        ["revision_queued"] = typeof(RevisionQueuedIssue),
        ["revision_in_progress"] = typeof(RevisionInProgressIssue),
        ["revision_failed"] = typeof(RevisionFailedIssue),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, Type> ResolvedMap = new Dictionary<string, Type>
    {
        ["completed"] = typeof(CompletedIssue),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, Type> AllMap = ActiveMap
        .Concat(ResolvedMap)
        .ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Returns the set of active state wire names.</summary>
    public static IReadOnlySet<string> Active { get; } = ActiveMap.Keys.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Returns the set of resolved state wire names.</summary>
    public static IReadOnlySet<string> Resolved { get; } = ResolvedMap.Keys.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Returns true when <paramref name="name"/> is a recognised wire name.</summary>
    public static bool IsKnownName(string name) => AllMap.ContainsKey(name);

    /// <summary>
    /// Returns the entity <see cref="Type"/> for a known wire <paramref name="name"/>,
    /// or <see langword="null"/> when the name is unknown.
    /// </summary>
    public static Type? GetEntityType(string name) =>
        AllMap.TryGetValue(name, out Type? type) ? type : null;
}
