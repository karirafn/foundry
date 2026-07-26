using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Foundry.Shared.Infrastructure;

public static class DbContextTransitionExtensions
{
    public static async Task TransitionAsync<TFrom, TTo>(
        this DbContext db,
        TFrom old,
        TTo next,
        IDomainEventDispatcher dispatcher,
        CancellationToken cancellationToken = default)
        where TFrom : class, IStateMachine, IDomainEventSource
        where TTo : class, IStateMachine
    {
        await using IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        db.Remove(old);
        await db.SaveChangesAsync(cancellationToken);

        db.Add(next);
        await db.SaveChangesAsync(cancellationToken);

        // Dispatch domain events inside the transaction. Any integration events enqueued
        // before TransitionAsync was called were already harvested by the interceptor on
        // the db.Remove/db.Add SaveChanges calls above. Bridge-handler events enqueued
        // during DispatchAsync are harvested on the trailing SaveChanges below. A handler
        // throw rolls back the entire transaction — no state change, no outbox row.
        await dispatcher.DispatchAsync(old.DomainEvents, cancellationToken);
        old.ClearDomainEvents();

        // Trailing harvest: drains bridge-raised integration events into outbox_messages.
        // All three saves share one transaction so committed state change plus all outbox
        // rows are one atomic unit. When no bridge events were enqueued this is a no-op.
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
