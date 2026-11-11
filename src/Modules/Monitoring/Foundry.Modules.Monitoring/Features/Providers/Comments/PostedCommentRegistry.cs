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
        List<ProviderCommentId> ids = await dbContext.Set<PostedComment>()
            .AsNoTracking()
            .Where(c => c.MonitoredRepositoryId == repositoryId)
            .Select(c => c.CommentId)
            .ToListAsync(cancellationToken);

        return new PostedCommentIds(ids);
    }

    /// <summary>
    /// Stages a registry entry for a comment just posted; the caller's save persists it, ideally together
    /// with the state change that records the post, so at most one spurious read survives a failed save.
    /// Recording the same comment twice is a no-op.
    /// </summary>
    public void Record(MonitoredRepositoryId repositoryId, ProviderCommentId commentId, DateTimeOffset postedAt)
    {
        PostedComment? staged = dbContext.Set<PostedComment>().Local
            .FirstOrDefault(c => c.MonitoredRepositoryId == repositoryId && c.CommentId == commentId);

        if (staged is not null)
        {
            return;
        }

        dbContext.Set<PostedComment>().Add(PostedComment.Record(repositoryId, commentId, postedAt));
    }
}
