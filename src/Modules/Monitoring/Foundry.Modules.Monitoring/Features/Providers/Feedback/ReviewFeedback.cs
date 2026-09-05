using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Providers.Feedback;

internal sealed record ReviewFeedback(
    IReadOnlyList<ReviewComment> Comments,
    int OmittedCommentCount,
    DateTimeOffset? NewestCommentAt);
