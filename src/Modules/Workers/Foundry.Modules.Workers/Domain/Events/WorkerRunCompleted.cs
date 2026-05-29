using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Events;

internal sealed record WorkerRunCompleted(
    WorkerRunId WorkerRunId,
    IssueId IssueId,
    string? BranchName,
    string? PullRequestUrl) : IDomainEvent;
