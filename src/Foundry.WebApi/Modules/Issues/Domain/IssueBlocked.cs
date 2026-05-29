using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.Shared;

namespace Foundry.WebApi.Modules.Issues.Domain;

public sealed record IssueBlocked(IssueId IssueId, MonitoredRepositoryId MonitoredRepositoryId) : IDomainEvent;
