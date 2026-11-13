using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts.Events;

/// <summary>
/// Raised when an issue whose run holds unpublished work (its publish was blocked or failed) completes because its
/// provider issue closed. That work will never be published, so Workers discards the run and deletes its Hand-off
/// volume rather than leaving it to the volume's time to live.
/// </summary>
public sealed record IssueClosedWithUnpublishedRun(WorkerRunId WorkerRunId) : IIntegrationEvent;
