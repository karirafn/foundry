using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

public sealed record WorkerCreditsExhausted(
    WorkerRunId WorkerRunId,
    Guid IssueId) : IIntegrationEvent;
