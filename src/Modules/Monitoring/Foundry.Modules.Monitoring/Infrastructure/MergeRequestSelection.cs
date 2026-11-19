using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure;

/// <param name="Head">The pull request's head commit; <see langword="null"/> when the provider omitted it or sent a malformed one.</param>
internal sealed record MergeRequestCandidate(
    MergeRequestPresence Presence,
    string WebUrl,
    DateTimeOffset UpdatedAt,
    CommitSha? Head);

/// <summary>
/// Picks the pull request a branch lookup reports, the same way for every provider. A branch name
/// can be reused, so a merged or closed pull request counts only when its head is the commit the run
/// published; an open one always counts.
/// </summary>
internal static class MergeRequestSelection
{
    private static readonly MergeRequestByBranch NoMergeRequest = new(MergeRequestPresence.None, null);

    public static MergeRequestByBranch Select(
        IReadOnlyList<MergeRequestCandidate> candidates,
        CommitSha? publishedHead)
    {
        MergeRequestCandidate? selected = publishedHead is { } head
            ? SelectForPublishedHead(candidates, head)
            : SelectAny(candidates);

        return selected is null
            ? NoMergeRequest
            : new MergeRequestByBranch(selected.Presence, selected.WebUrl);
    }

    private static MergeRequestCandidate? SelectForPublishedHead(
        IReadOnlyList<MergeRequestCandidate> candidates,
        CommitSha publishedHead)
    {
        return candidates.FirstOrDefault(c => c.Presence is MergeRequestPresence.Merged && c.Head == publishedHead)
            ?? candidates.Where(c => c.Presence is MergeRequestPresence.Open).MaxBy(c => c.UpdatedAt)
            ?? candidates
                .Where(c => c.Presence is MergeRequestPresence.Closed && c.Head == publishedHead)
                .MaxBy(c => c.UpdatedAt);
    }

    private static MergeRequestCandidate? SelectAny(IReadOnlyList<MergeRequestCandidate> candidates)
    {
        return candidates.FirstOrDefault(c => c.Presence is MergeRequestPresence.Merged)
            ?? candidates.MaxBy(c => c.UpdatedAt);
    }
}
