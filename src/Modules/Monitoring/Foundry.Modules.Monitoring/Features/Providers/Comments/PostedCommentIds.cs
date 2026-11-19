using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;

namespace Foundry.Modules.Monitoring.Features.Providers.Comments;

/// <summary>
/// A snapshot of one repository's posted-comment registry: the IDs of the comments Foundry posted, which
/// every comment reader skips whoever their author is.
/// </summary>
internal sealed class PostedCommentIds
{
    private readonly HashSet<PostedCommentKey> _keys;

    public PostedCommentIds(IEnumerable<PostedCommentKey> keys)
    {
        _keys = [.. keys];
    }

    public static PostedCommentIds None { get; } = new([]);

    // An ID is matched only on the surface its kind names: GitHub numbers review comments and reviews independently
    // of issue comments, so matching an ID on another surface could skip a reviewer's comment that merely shares the
    // number. A person's reply in a registered review's thread belongs to the person's own review, and a GitLab reply
    // is its own note, so neither matches.
    public bool Contains(ProviderComment comment) => comment.Origin switch
    {
        CommentOrigin.Conversation => Has(PostedCommentKind.Conversation, comment.Id),
        CommentOrigin.ReviewThread => Has(PostedCommentKind.ReviewNote, comment.Id) || Has(PostedCommentKind.Review, comment.ReviewId),
        CommentOrigin.ReviewSummary => Has(PostedCommentKind.Review, comment.ReviewId),
        _ => false,
    };

    private bool Has(PostedCommentKind kind, ProviderCommentId? id) =>
        id is { } known && _keys.Contains(new PostedCommentKey(kind, known));
}
