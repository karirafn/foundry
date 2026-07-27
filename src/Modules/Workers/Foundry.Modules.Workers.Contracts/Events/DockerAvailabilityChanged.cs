using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts.Events;

public sealed record DockerAvailabilityChanged(bool IsAvailable) : IIntegrationEvent;
