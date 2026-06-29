using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Events;

internal sealed record WorkerActivityObserved(
    WorkerRunId WorkerRunId,
    IssueId IssueId,
    DateTimeOffset LastActivityAt,
    CommitMarker? NewCommitMarker) : IDomainEvent;
