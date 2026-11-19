using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Domain.Entities;

/// <summary>
/// One entry in the posted-comment registry: a comment Foundry posted through
/// <c>IIssueProvider</c>, keyed by repository, the kind of ID and the provider's ID. Every comment
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

    public PostedCommentKind Kind { get; private set; }

    public ProviderCommentId CommentId { get; private set; }

    public DateTimeOffset PostedAt { get; private set; }

    public static PostedComment Record(
        MonitoredRepositoryId repositoryId,
        PostedCommentKey key,
        DateTimeOffset postedAt) =>
        new()
        {
            MonitoredRepositoryId = repositoryId,
            Kind = key.Kind,
            CommentId = key.Id,
            PostedAt = postedAt,
        };
}
