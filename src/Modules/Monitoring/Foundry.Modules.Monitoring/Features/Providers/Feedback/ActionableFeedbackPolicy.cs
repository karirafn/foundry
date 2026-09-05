using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Providers.Feedback;

internal sealed class ActionableFeedbackPolicy(TimeProvider timeProvider)
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMinutes(2);
    private const int MaxComments = 50;

    public ReviewFeedback Apply(IReadOnlyList<ProviderComment> comments, DateTimeOffset since)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset quietCutoff = now - QuietPeriod;

        List<ProviderComment> survivors = comments
            .Where(c => !c.IsSystem)
            .Where(c => !c.AuthorIsBot)
            .Where(c => !c.ThreadResolved)
            .Where(c => c.CreatedAt > since)
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

        return new ReviewFeedback(reviewComments, omittedCommentCount, newestCommentAt);
    }
}
