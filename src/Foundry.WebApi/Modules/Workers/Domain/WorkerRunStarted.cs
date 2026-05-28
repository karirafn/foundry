using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Workers.Domain;

public sealed record WorkerRunStarted(WorkerRunId WorkerRunId, IssueId IssueId) : IDomainEvent;
