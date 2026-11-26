using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts.Events;

/// <summary>
/// A ready triage verdict settled details itself (ADR 0123). Monitoring posts them on the issue thread at once, so a
/// person can veto them with Re-triage before work starts.
/// </summary>
/// <param name="Decisions">The decisions in the order triage listed them; at least one.</param>
/// <param name="Release">Where the dependency check sent the issue after the verdict.</param>
/// <param name="Demand">The verdict's Implement Demand; an event queued before it was carried reads as demanding.</param>
/// <param name="DemandReason">Triage's one-line reason for the demand, or null when it gave none.</param>
public sealed record TriageDecisionsRecorded(
    Guid IssueId,
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    IReadOnlyList<TriageDecision> Decisions,
    TriageRelease Release,
    ImplementDemand Demand = ImplementDemand.Demanding,
    string? DemandReason = null) : IIntegrationEvent;

/// <summary>Where a ready triage verdict left its issue: queued for work, or waiting on recorded blockers.</summary>
public enum TriageRelease
{
    Queued,
    WaitingOnBlockers,
}
