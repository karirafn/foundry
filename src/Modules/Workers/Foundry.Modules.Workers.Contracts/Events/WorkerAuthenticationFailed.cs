using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts.Events;

public sealed record WorkerAuthenticationFailed(
    Guid WorkerRunId,
    Guid IssueId,
    string Reason) : IIntegrationEvent;
