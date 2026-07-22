using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Shared.Infrastructure;

/// <summary>
/// Wraps <see cref="IDomainEventDispatcher"/> and swallows handler exceptions so that a
/// failure in a domain-event handler does not crash the caller.
/// </summary>
/// <remarks>
/// <para>
/// Step 9 will replace or remove this wrapper once all bridge handlers enqueue into the
/// transactional outbox. With the D2 ordering in <c>TransitionAsync</c>, domain events are
/// dispatched <em>inside</em> the transaction: a handler throw rolls back the entire
/// transaction (state change + outbox row). Swallowing the throw here prevents the rollback,
/// so the state change commits but no outbox message is written — integration event is silently
/// lost. Until step 9 completes, this wrapper is kept as-is for backward compatibility.
/// </para>
/// </remarks>
public sealed class FaultTolerantDomainEventDispatcher(
    IDomainEventDispatcher inner,
    ILogger logger) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
    {
        IReadOnlyList<IDomainEvent> eventList = events as IReadOnlyList<IDomainEvent> ?? events.ToList();

        try
        {
            await inner.DispatchAsync(eventList, cancellationToken);
        }
#pragma warning disable CA1031 // Swallowing bridge-handler failures is intentional for backward compatibility until step 9 migrates all bridge handlers to the outbox; see remarks on FaultTolerantDomainEventDispatcher.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            foreach (IDomainEvent @event in eventList)
            {
                logger.LogWarning(
                    ex,
                    "Failed to dispatch domain event {EventType} after state transition.",
                    @event.GetType().Name);
            }
        }
    }
}
