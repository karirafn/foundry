using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.Events;

/// <summary>
/// A <c>publish_blocked</c> or <c>publish_failed</c> issue completed because its provider issue closed, so its run's
/// unpublished work will never be published and the run can be discarded.
/// </summary>
public sealed record IssueClosedWithUnpublishedRun(
    IssueId IssueId,
    MonitoredRepositoryId MonitoredRepositoryId,
    WorkerRunId WorkerRunId) : IDomainEvent;
