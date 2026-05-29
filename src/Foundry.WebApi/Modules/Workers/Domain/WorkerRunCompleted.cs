using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;

namespace Foundry.WebApi.Modules.Workers.Domain;

public sealed record WorkerRunCompleted(
    WorkerRunId WorkerRunId,
    IssueId IssueId,
    string? BranchName,
    string? PullRequestUrl) : IDomainEvent;
