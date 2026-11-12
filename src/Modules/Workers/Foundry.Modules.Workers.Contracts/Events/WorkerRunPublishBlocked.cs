using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// The Diff gate found possible secrets in a run's commits, so nothing was pushed (#788). The run keeps its unpublished
/// work until an operator discards it (or its Hand-off volume's time to live does), so its issue needs attention. The
/// event carries only the number of findings, never what they matched.
/// </summary>
public sealed record WorkerRunPublishBlocked(WorkerRunId WorkerRunId, Guid IssueId, string BranchName, int FindingCount)
    : IIntegrationEvent;
