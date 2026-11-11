using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Providers.Comments;

namespace Foundry.Modules.Monitoring.Features.Providers.Feedback;

internal sealed class ActionableFeedbackPolicy(TimeProvider timeProvider)
{
    /// <summary>How long a comment must age before it is actionable, so a burst of comments is consumed together.</summary>
    internal static readonly TimeSpan QuietPeriod = TimeSpan.FromMinutes(2);
    private const int MaxComments = 50;

    public ReviewFeedback Apply(
        IReadOnlyList<ProviderComment> comments,
        DateTimeOffset since,
        PostedCommentIds postedComments)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset quietCutoff = now - QuietPeriod;

        List<ProviderComment> candidates = comments
            .Where(c => !postedComments.Contains(c))
            .Where(c => !c.IsSystem)
            .Where(c => !c.AuthorIsBot)
            .Where(c => !c.ThreadResolved)
            .Where(c => c.CreatedAt > since)
            .ToList();

        // Comments held back only by the quiet period: the newest one decides when they all become actionable.
        DateTimeOffset? newestHeldAt = candidates
            .Where(c => c.CreatedAt > quietCutoff)
            .Select(c => (DateTimeOffset?)c.CreatedAt)
            .Max();

        List<ProviderComment> survivors = candidates
            .Where(c => c.CreatedAt <= quietCutoff)
            .OrderByDescending(c => c.CreatedAt)
            .ToList();

        int survivorsBeforeCap = survivors.Count;
        List<ProviderComment> kept = survivors.Take(MaxComments).ToList();
        int omittedCommentCount = Math.Max(0, survivorsBeforeCap - MaxComments);

        DateTimeOffset? newestCommentAt = kept.Count > 0
            ? kept.Max(c => c.CreatedAt)
            : null;

        // Reverse to chronological (oldest-first) order for the prompt.
        kept.Reverse();

        List<ReviewComment> reviewComments = kept
            .Select(c => new ReviewComment(c.Body, c.FilePath, c.Line))
            .ToList();

        return new ReviewFeedback(
            reviewComments,
            omittedCommentCount,
            newestCommentAt,
            newestHeldAt + QuietPeriod);
    }
}
