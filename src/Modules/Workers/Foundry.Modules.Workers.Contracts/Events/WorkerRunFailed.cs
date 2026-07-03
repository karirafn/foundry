using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

public sealed record WorkerRunFailed(
    Guid WorkerRunId,
    Guid IssueId,
    string ReasonDescription,
    string? Category = null,
    string? BranchName = null) : IIntegrationEvent
{
    public const string UsageLimitedReason = "Usage limit reached";

    public const string AuthInvalidReason = "Worker authentication failed";
}
