using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public sealed record ClaimSkipped(WorkerRunId WorkerRunId) : IIntegrationEvent;
