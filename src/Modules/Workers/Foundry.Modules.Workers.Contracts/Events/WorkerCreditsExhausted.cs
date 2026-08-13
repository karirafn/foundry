using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

public sealed record WorkerCreditsExhausted(
    Guid WorkerRunId,
    Guid IssueId) : IIntegrationEvent;
