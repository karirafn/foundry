using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

public sealed record WorkerRunCompleted(
    Guid WorkerRunId,
    Guid IssueId,
    string? BranchName,
    string? PullRequestUrl) : IIntegrationEvent;
