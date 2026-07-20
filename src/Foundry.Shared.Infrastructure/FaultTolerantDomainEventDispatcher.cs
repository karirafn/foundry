using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Shared.Infrastructure;

/// <summary>
/// Wraps <see cref="IDomainEventDispatcher"/> and swallows handler exceptions so that a
/// failure in a domain-event handler does not crash the caller. The tick commits state via
/// <c>TransitionAsync</c> before dispatching domain events, so a bridge-handler throw must
/// not crash the BackgroundService tick. Contrast with <see cref="DomainEventDispatcher"/>
/// used in <c>IssueClaimedHandler</c>, which deliberately uses the raw dispatcher so the
/// integration-event bus governs its own error handling.
/// </summary>
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
#pragma warning disable CA1031 // Domain-event handler failures (e.g. bridge that publishes integration event) must not crash the BackgroundService tick; the transition has already committed and the warning log surfaces the failure.
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
