using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Events;

public sealed record TriageBlockersNamed(
    IssueId IssueId,
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    WorkerRunId WorkerRunId,
    IReadOnlyList<TriageBlocker> Blockers) : IDomainEvent;
