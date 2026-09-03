using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Events;

internal sealed record WorkerRunFailed(
    WorkerRunId WorkerRunId,
    IssueId IssueId,
    string ReasonDescription,
    string Category,
    string? BranchName) : IDomainEvent;
