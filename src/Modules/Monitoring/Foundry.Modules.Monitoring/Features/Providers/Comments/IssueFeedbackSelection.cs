using Foundry.Modules.Monitoring.Features.Providers.Feedback;

namespace Foundry.Modules.Monitoring.Features.Providers.Comments;

/// <summary>
/// The provider-agnostic rule of the issue-thread reader: comments created after the cutoff that Foundry did
/// not post, oldest first. Bot and system comments stay in, flagged, for callers to judge.
/// </summary>
internal static class IssueFeedbackSelection
{
    public static IReadOnlyList<ProviderComment> Apply(
        IReadOnlyList<ProviderComment> comments,
        DateTimeOffset since,
        PostedCommentIds postedComments) =>
        comments
            .Where(c => c.CreatedAt > since)
            .Where(c => !postedComments.Contains(c))
            .OrderBy(c => c.CreatedAt)
            .ToList();
}
