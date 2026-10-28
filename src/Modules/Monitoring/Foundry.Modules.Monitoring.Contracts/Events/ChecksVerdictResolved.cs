using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

public sealed record ChecksVerdictResolved(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    string Outcome,
    IReadOnlyList<FailingCheck> FailingChecks) : IIntegrationEvent;
