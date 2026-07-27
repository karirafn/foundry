using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts.Events;

public sealed record WorkerCapacityAvailable(Guid WorkerRunId) : IIntegrationEvent;
