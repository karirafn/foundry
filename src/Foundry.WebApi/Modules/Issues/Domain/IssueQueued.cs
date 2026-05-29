using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.Shared;

namespace Foundry.WebApi.Modules.Issues.Domain;

public sealed record IssueQueued(IssueId IssueId, MonitoredRepositoryId MonitoredRepositoryId) : IDomainEvent;
