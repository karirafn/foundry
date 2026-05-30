using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Events;

public sealed record IssueInReview(
    IssueId IssueId,
    MonitoredRepositoryId MonitoredRepositoryId) : IDomainEvent;
