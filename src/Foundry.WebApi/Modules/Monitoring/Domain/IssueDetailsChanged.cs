using Foundry.Shared;

namespace Foundry.WebApi.Modules.Monitoring.Domain;

public sealed record IssueDetailsChanged(
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    string Title,
    string Body,
    IReadOnlyList<string> Labels) : IDomainEvent;
