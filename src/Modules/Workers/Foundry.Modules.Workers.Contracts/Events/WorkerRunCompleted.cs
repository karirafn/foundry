using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

/// <param name="InputRequest">
/// Set only when the run exited 0 with a schema-valid <c>needs_input</c> verdict (ADR 0096): the run stopped to ask
/// a person, and the issue waits for input instead of moving on by <paramref name="MergeState"/>.
/// </param>
public sealed record WorkerRunCompleted(
    WorkerRunId WorkerRunId,
    Guid IssueId,
    string? BranchName,
    string? PullRequestUrl,
    WorkerRunMergeState MergeState,
    DateTimeOffset? RunStartedAt = null,
    WorkerInputRequest? InputRequest = null) : IIntegrationEvent;
