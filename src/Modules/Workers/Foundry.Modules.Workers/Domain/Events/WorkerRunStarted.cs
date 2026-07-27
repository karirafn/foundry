using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;
using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Domain.Events;

internal sealed record WorkerRunStarted(WorkerRunId WorkerRunId, IssueId IssueId) : IDomainEvent;
