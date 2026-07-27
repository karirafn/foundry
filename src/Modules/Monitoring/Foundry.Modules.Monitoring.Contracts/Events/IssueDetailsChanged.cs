using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Events;

public sealed record IssueDetailsChanged(
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    string Title,
    string Body,
    IReadOnlyList<string> Labels) : IIntegrationEvent;
