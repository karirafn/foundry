using Foundry.Modules.Issues.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Features.StateChanges;

internal sealed class IssueStateChangedAdapter<T>(IssueStateChangedHandler handler)
    : IDomainEventHandler<T>
    where T : IDomainEvent, IIssueStateChanged
{
    public Task HandleAsync(T @event, CancellationToken cancellationToken)
        => handler.HandleAsync(@event, cancellationToken);
}
