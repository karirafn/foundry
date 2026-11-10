using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Foundry.Shared.Infrastructure;

public static class DbContextTransitionExtensions
{
    public static Task TransitionAsync<TFrom, TTo>(
        this DbContext db,
        TFrom old,
        TTo next,
        IDomainEventDispatcher dispatcher,
        CancellationToken cancellationToken = default)
        where TFrom : class, IStateMachine, IDomainEventSource
        where TTo : class, IStateMachine
    {
        return db.TransitionAsync(old, next, alongside: [], dispatcher, cancellationToken);
    }

    /// <summary>
    /// Transitions <paramref name="old"/> to <paramref name="next"/>, adding <paramref name="alongside"/>
    /// in the same save as <paramref name="next"/>. Entities keyed to the state row must be added here
    /// rather than before the call: they would be added while <paramref name="old"/> is being removed.
    /// </summary>
    public static async Task TransitionAsync<TFrom, TTo>(
        this DbContext db,
        TFrom old,
        TTo next,
        IReadOnlyCollection<object> alongside,
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

        foreach (object entity in alongside)
        {
            db.Add(entity);
        }

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
