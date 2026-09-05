using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

public sealed record IssueDetected(
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    string Title,
    string Author,
    string Url,
    IReadOnlyList<string> Labels,
    string IssueKindLabel,
    DateTimeOffset DetectedAt) : IIntegrationEvent;
