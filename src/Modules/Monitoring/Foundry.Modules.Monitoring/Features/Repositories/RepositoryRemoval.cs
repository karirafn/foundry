using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Webhooks.Registration;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Foundry.Modules.Monitoring.Features.Repositories;

/// <summary>
/// The single path that removes monitored repositories (ADR 0117): each is marked removed, its repository-owned
/// rows are deleted, the survivors are renumbered and its removal event is dispatched, all in one transaction.
/// Removal is refused while any issue of the repositories has work in flight.
/// </summary>
internal sealed class RepositoryRemoval(
    DbContext dbContext,
    RepositoryWebhookDeleter webhookDeleter,
    IRepositoryRemovalBlockers removalBlockers,
    IDomainEventDispatcher domainEventDispatcher)
{
    public async Task<Result<bool>> RemoveAsync(
        IReadOnlyCollection<MonitoredRepository> repositories,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (repositories.FirstOrDefault(r => r.RemovedAt is not null) is not null)
        {
            return Result<bool>.Fail(MonitoredRepositoryErrors.AlreadyRemoved());
        }

        // Checked ahead of the hook deletion too, so a refused removal never deletes hooks.
        if (await FindBlockersAsync(repositories, cancellationToken) is { } refusedEarly)
        {
            return Result<bool>.Fail(refusedEarly);
        }

        // Outside the transaction, so a slow provider never holds the database, and while the covering account
        // still exists; failures never block removal.
        foreach (MonitoredRepository repository in repositories)
        {
            await webhookDeleter.DeleteAsync(repository, cancellationToken);
        }

        await using IDbContextTransaction tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Authoritative: a claim checks the repository is not removed inside its own write transaction, and SQLite
        // serializes the two, so either the claim committed first and is seen here, or it sees this removal.
        if (await FindBlockersAsync(repositories, cancellationToken) is { } refused)
        {
            return Result<bool>.Fail(refused);
        }

        foreach (MonitoredRepository repository in repositories)
        {
            if (repository.Remove(now) is Result.Failure failure)
            {
                return Result<bool>.Fail(failure.Error);
            }
        }

        List<MonitoredRepositoryId> repositoryIds = repositories.Select(r => r.Id).ToList();
        await DeleteOwnedRowsAsync(repositoryIds, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        List<MonitoredRepository> survivors = await dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(cancellationToken);

        await RepositoryRenumber.RenumberAsync(dbContext, survivors, cancellationToken);

        foreach (MonitoredRepository repository in repositories)
        {
            await domainEventDispatcher.DispatchAsync(repository.DomainEvents, cancellationToken);
            repository.ClearDomainEvents();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    private async Task<RemovalBlockedError?> FindBlockersAsync(
        IReadOnlyCollection<MonitoredRepository> repositories,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RemovalBlockingIssue> blocking = await removalBlockers.GetBlockingIssuesAsync(
            [.. repositories.Select(r => r.Id)],
            cancellationToken);

        if (blocking.Count == 0)
        {
            return null;
        }

        Dictionary<MonitoredRepositoryId, string> slugs = repositories.ToDictionary(r => r.Id, r => r.Slug.FullPath);
        return RepositoryErrors.RemovalBlocked(
        [
            .. blocking.Select(b => new RemovalBlocker(slugs[b.RepositoryId], b.IssueNumber, b.State)),
        ]);
    }

    // The rows a hard delete removed through the cascading foreign keys to the repository.
    private async Task DeleteOwnedRowsAsync(List<MonitoredRepositoryId> repositoryIds, CancellationToken cancellationToken)
    {
        await dbContext.Set<IssueWatch>()
            .Where(w => repositoryIds.Contains(w.MonitoredRepositoryId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<PendingMerge>()
            .Where(m => repositoryIds.Contains(m.MonitoredRepositoryId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<PendingRebase>()
            .Where(r => repositoryIds.Contains(r.MonitoredRepositoryId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<PostedComment>()
            .Where(c => repositoryIds.Contains(c.MonitoredRepositoryId))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
