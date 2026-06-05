using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

public sealed record WorkerRunFailed(
    Guid WorkerRunId,
    Guid IssueId,
    string ReasonDescription)
    : IIntegrationEvent
{
    public string? BranchName { get; init; }

    public string? LatestProgress { get; init; }
}
