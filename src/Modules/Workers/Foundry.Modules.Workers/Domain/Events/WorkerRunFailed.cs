using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;
using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Domain.Events;

internal sealed record WorkerRunFailed(
    WorkerRunId WorkerRunId,
    IssueId IssueId,
    string ReasonDescription,
    string Category,
    string? BranchName) : IDomainEvent;
