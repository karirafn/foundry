using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Domain.Entities;

/// <summary>
/// One entry in the posted-comment registry: a comment Foundry posted through
/// <c>IIssueProvider.PostCommentAsync</c>, keyed by repository and provider comment ID. Every comment
/// reader excludes registered comments by ID rather than by author, so the PAT owner's own replies are
/// still read.
/// </summary>
public sealed class PostedComment
{
    // Private parameterless constructor for EF Core materialization.
    private PostedComment()
    {
    }

    public MonitoredRepositoryId MonitoredRepositoryId { get; private set; }

    public ProviderCommentId CommentId { get; private set; }

    public DateTimeOffset PostedAt { get; private set; }

    public static PostedComment Record(
        MonitoredRepositoryId repositoryId,
        ProviderCommentId commentId,
        DateTimeOffset postedAt) =>
        new()
        {
            MonitoredRepositoryId = repositoryId,
            CommentId = commentId,
            PostedAt = postedAt,
        };
}
