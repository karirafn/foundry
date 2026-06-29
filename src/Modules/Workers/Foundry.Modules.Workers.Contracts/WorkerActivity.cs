namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Payload broadcast to the dashboard when a worker emits new log output or a new commit is observed.
/// Step 8 will formalize this DTO — fields are intentionally minimal for the initial broadcast.
/// </summary>
public sealed record WorkerActivity(
    Guid WorkerRunId,
    Guid IssueId,
    DateTimeOffset LastActivityAt,
    string? NewCommitSha,
    string? NewCommitMessage);
