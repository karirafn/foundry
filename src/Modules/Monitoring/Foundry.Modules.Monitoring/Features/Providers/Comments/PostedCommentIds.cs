using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;

namespace Foundry.Modules.Monitoring.Features.Providers.Comments;

/// <summary>
/// A snapshot of one repository's posted-comment registry: the IDs of the comments Foundry posted, which
/// every comment reader skips whoever their author is.
/// </summary>
internal sealed class PostedCommentIds
{
    private readonly HashSet<ProviderCommentId> _ids;

    public PostedCommentIds(IEnumerable<ProviderCommentId> ids)
    {
        _ids = [.. ids];
    }

    public static PostedCommentIds None { get; } = new([]);

    // Foundry only posts top-level thread comments, which providers report on the conversation surface.
    // GitHub numbers review comments and reviews independently of issue comments, so matching an ID on any
    // other surface could skip a reviewer's comment that merely shares the number.
    public bool Contains(ProviderComment comment) =>
        comment.Origin == CommentOrigin.Conversation
        && comment.Id is { } id
        && _ids.Contains(id);
}
