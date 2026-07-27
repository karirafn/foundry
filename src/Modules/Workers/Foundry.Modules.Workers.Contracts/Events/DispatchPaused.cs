using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts.Events;

public sealed record DispatchPaused(DateTimeOffset ResetsAt) : IIntegrationEvent;
