using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// An operator confirmed a publish-blocked run's secret findings as false positives (#899); the run is publishing again
/// with exactly those findings suppressed, so its issue returns to work in progress.
/// </summary>
public sealed record WorkerRunFalsePositivesConfirmed(WorkerRunId WorkerRunId, Guid IssueId) : IIntegrationEvent;
