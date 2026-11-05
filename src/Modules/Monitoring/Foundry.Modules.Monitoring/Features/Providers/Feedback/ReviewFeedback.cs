using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Providers.Feedback;

/// <summary>
/// The actionable review feedback on a pull request. <see cref="HeldBackUntil"/> is when the newest
/// comment held back only by the quiet period becomes actionable (its <c>CreatedAt</c> plus the quiet
/// period), or <see langword="null"/> when no comment is held back — the review pass schedules a
/// follow-up nudge for that time (issue #702).
/// </summary>
internal sealed record ReviewFeedback(
    IReadOnlyList<ReviewComment> Comments,
    int OmittedCommentCount,
    DateTimeOffset? NewestCommentAt,
    DateTimeOffset? HeldBackUntil = null);
