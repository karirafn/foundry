using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// A run entered or left a publish phase. <paramref name="Phase"/> is <see langword="null"/> when the run left
/// publishing (completed, failed, or exhausted its retries). The issue list refreshes on it.
/// </summary>
public sealed record WorkerRunPublishPhaseChanged(
    WorkerRunId WorkerRunId,
    Guid IssueId,
    WorkerRunPublishPhase? Phase) : IIntegrationEvent;
