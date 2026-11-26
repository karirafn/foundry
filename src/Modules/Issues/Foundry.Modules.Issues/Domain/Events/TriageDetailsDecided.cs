using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Events;

public sealed record TriageDetailsDecided(
    IssueId IssueId,
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    IReadOnlyList<TriageDecision> Decisions,
    TriageRelease Release,
    ImplementDemand Demand,
    string? DemandReason) : IDomainEvent;
