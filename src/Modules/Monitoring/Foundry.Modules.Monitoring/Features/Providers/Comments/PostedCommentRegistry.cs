using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Providers.Comments;

/// <summary>The posted-comment registry: the comments Foundry posted, which every comment reader skips.</summary>
internal sealed class PostedCommentRegistry(DbContext dbContext)
{
    public async Task<PostedCommentIds> LoadAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        List<PostedComment> entries = await dbContext.Set<PostedComment>()
            .AsNoTracking()
            .Where(c => c.MonitoredRepositoryId == repositoryId)
            .ToListAsync(cancellationToken);

        return new PostedCommentIds(entries.Select(c => new PostedCommentKey(c.Kind, c.CommentId)));
    }

    /// <summary>
    /// Stages a registry entry for a comment just posted; the caller's save persists it, ideally together
    /// with the state change that records the post, so at most one spurious read survives a failed save.
    /// Recording the same comment twice is a no-op.
    /// </summary>
    public void Record(MonitoredRepositoryId repositoryId, ProviderCommentId commentId, DateTimeOffset postedAt) =>
        Record(repositoryId, PostedCommentKey.Conversation(commentId), postedAt);

    /// <inheritdoc cref="Record(MonitoredRepositoryId, ProviderCommentId, DateTimeOffset)"/>
    public void Record(MonitoredRepositoryId repositoryId, PostedCommentKey key, DateTimeOffset postedAt)
    {
        PostedComment? staged = dbContext.Set<PostedComment>().Local
            .FirstOrDefault(c => c.MonitoredRepositoryId == repositoryId && c.Kind == key.Kind && c.CommentId == key.Id);

        if (staged is not null)
        {
            return;
        }

        dbContext.Set<PostedComment>().Add(PostedComment.Record(repositoryId, key, postedAt));
    }
}
