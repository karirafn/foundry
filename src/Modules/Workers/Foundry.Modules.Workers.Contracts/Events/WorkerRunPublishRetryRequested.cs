using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// An operator asked Foundry to publish a run again after its publish retries were exhausted; the run is publishing
/// again from its retained Hand-off volume, so its issue returns to work in progress.
/// </summary>
public sealed record WorkerRunPublishRetryRequested(WorkerRunId WorkerRunId, Guid IssueId) : IIntegrationEvent;
