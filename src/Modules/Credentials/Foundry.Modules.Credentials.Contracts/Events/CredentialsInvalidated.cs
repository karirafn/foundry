using Foundry.Shared;

namespace Foundry.Modules.Credentials.Contracts.Events;

public sealed record CredentialsInvalidated(string Reason) : IIntegrationEvent;
