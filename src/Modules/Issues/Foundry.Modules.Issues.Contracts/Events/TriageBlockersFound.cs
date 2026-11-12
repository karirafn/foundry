using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts.Events;

/// <summary>
/// An issue was held on the open issues its triage run named (ADR 0103). Monitoring links each blocker on the
/// provider and posts one comment once every link is written.
/// </summary>
/// <param name="WorkerRunId">The triage run that named the blockers, so a failed link fails that run's verdict.</param>
/// <param name="Blockers">The named blockers in the order triage listed them.</param>
public sealed record TriageBlockersFound(
    Guid IssueId,
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    WorkerRunId WorkerRunId,
    IReadOnlyList<TriageBlocker> Blockers) : IIntegrationEvent;
