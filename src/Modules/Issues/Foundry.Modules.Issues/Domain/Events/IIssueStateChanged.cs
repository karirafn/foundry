using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Events;

// Marker interface for domain events that represent an issue state transition
#pragma warning disable CA1040
public interface IIssueStateChanged : IDomainEvent
{
    IssueId IssueId { get; }
}
#pragma warning restore CA1040
