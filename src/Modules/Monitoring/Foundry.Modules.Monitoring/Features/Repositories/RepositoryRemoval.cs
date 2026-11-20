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
/// </summary>
internal sealed class RepositoryRemoval(
    DbContext dbContext,
    RepositoryWebhookDeleter webhookDeleter,
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

        // Outside the transaction, so a slow provider never holds the database, and while the covering account
        // still exists; failures never block removal.
        foreach (MonitoredRepository repository in repositories)
        {
            await webhookDeleter.DeleteAsync(repository, cancellationToken);
        }

        await using IDbContextTransaction tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

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
