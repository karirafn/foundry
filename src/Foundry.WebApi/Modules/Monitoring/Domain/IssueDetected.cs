using Foundry.Shared;

namespace Foundry.WebApi.Modules.Monitoring.Domain;

public sealed record IssueDetected(
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    string Title,
    string Body,
    string Author,
    string Url,
    IReadOnlyList<string> Labels,
    DateTimeOffset DetectedAt) : IDomainEvent;
