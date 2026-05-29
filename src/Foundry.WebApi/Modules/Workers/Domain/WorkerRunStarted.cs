using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;

namespace Foundry.WebApi.Modules.Workers.Domain;

public sealed record WorkerRunStarted(WorkerRunId WorkerRunId, IssueId IssueId) : IDomainEvent;
