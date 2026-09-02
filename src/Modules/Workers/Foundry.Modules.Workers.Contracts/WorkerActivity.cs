namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Payload broadcast to the dashboard when a worker emits new log output or a new commit is observed.
/// </summary>
public sealed record WorkerActivity(
    WorkerRunId WorkerRunId,
    Guid IssueId,
    DateTimeOffset LastActivityAt,
    int CommitCount);
