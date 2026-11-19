namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// One posted-comment registry entry's key within a repository. GitHub numbers reviews apart from issue comments, so an
/// ID means nothing without its kind.
/// </summary>
public readonly record struct PostedCommentKey(PostedCommentKind Kind, ProviderCommentId Id)
{
    public static PostedCommentKey Conversation(ProviderCommentId id) => new(PostedCommentKind.Conversation, id);

    public static PostedCommentKey Review(ProviderCommentId id) => new(PostedCommentKind.Review, id);

    public static PostedCommentKey ReviewNote(ProviderCommentId id) => new(PostedCommentKind.ReviewNote, id);
}
