using Foundry.Shared;

namespace Foundry.Modules.Credentials.Contracts;

public sealed record CredentialsInvalidated(string Reason) : IIntegrationEvent;
