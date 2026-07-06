using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

public sealed record WorkerAuthenticationFailed(
    Guid WorkerRunId,
    Guid IssueId,
    string Reason) : IIntegrationEvent;
