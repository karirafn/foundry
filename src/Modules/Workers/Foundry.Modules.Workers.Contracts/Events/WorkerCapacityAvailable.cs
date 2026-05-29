using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

public sealed record WorkerCapacityAvailable(Guid WorkerRunId) : IIntegrationEvent;
