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

        // Dispatch domain events inside the transaction so that any integration events
        // enqueued by bridge handlers are captured by the OutboxSaveChangesInterceptor
        // on the harvest SaveChanges below. A handler throw rolls back the entire
        // transaction — no state change, no outbox row.
        await dispatcher.DispatchAsync(old.DomainEvents, cancellationToken);
        old.ClearDomainEvents();

        // Harvest: the interceptor drains any enqueued integration events into
        // outbox_messages atomically with the state change committed above.
        // When no events were enqueued, the collector is empty and this is a no-op.
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
