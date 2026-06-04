using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Events;

public sealed record IssueEligibilityChecked(
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    IReadOnlyList<EligibilityViolationInfo> Violations) : IIntegrationEvent;
