using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Events;

public sealed record IssueDetected(
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    string Title,
    string Body,
    string Author,
    string Url,
    IReadOnlyList<string> Labels,
    string IssueKindLabel,
    DateTimeOffset DetectedAt) : IIntegrationEvent;
