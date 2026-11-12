namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Payload broadcast to the dashboard each time a running worker is observed alive.
/// </summary>
public sealed record WorkerActivity(
    WorkerRunId WorkerRunId,
    Guid IssueId,
    DateTimeOffset LastActivityAt);
