using Foundry.Shared;

namespace Foundry.Modules.Credentials.Contracts.Events;

public sealed record CredentialsValidated(
    string? Email,
    string? OrgName,
    string? SubscriptionType) : IIntegrationEvent;
