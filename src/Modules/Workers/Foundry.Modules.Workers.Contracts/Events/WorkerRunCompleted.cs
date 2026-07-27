using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts.Events;

public sealed record WorkerRunCompleted(
    Guid WorkerRunId,
    Guid IssueId,
    string? BranchName,
    string? PullRequestUrl,
    WorkerRunMergeState MergeState) : IIntegrationEvent;
