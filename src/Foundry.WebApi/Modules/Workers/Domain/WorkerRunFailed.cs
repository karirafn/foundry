using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Workers.Domain;

public sealed record WorkerRunFailed(
    WorkerRunId WorkerRunId,
    IssueId IssueId,
    string ReasonDescription) : IDomainEvent;
