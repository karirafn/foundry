using Foundry.Shared;

namespace Foundry.Modules.Credentials.Contracts;

public sealed record UsageLimitReached(DateTimeOffset ResetsAt) : IIntegrationEvent;
