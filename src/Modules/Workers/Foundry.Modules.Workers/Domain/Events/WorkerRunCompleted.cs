using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Events;

internal sealed record WorkerRunCompleted(
    WorkerRunId WorkerRunId,
    IssueId IssueId,
    string? BranchName,
    string? PullRequestUrl) : IDomainEvent;
