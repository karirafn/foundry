using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

public sealed record DockerAvailabilityChanged(bool IsAvailable) : IIntegrationEvent;
