using Foundry.Shared;

namespace Foundry.Modules.Credentials.Contracts;

public sealed record CreditsRestored(
    string? Email,
    string? OrgName,
    string? SubscriptionType) : IIntegrationEvent;
