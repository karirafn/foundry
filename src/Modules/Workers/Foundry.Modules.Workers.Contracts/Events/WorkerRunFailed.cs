using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

public sealed record WorkerRunFailed(
    Guid WorkerRunId,
    Guid IssueId,
    string ReasonDescription,
    string? BranchName = null) : IIntegrationEvent;
