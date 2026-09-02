using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

public sealed record WorkerAuthenticationFailed(
    WorkerRunId WorkerRunId,
    Guid IssueId,
    string Reason) : IIntegrationEvent;
