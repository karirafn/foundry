namespace Foundry.Modules.Credentials.Contracts;

public sealed record ClaudeAccountSummary(
    Guid AccountId,
    string AuthMode,
    string OAuthStatus,
    string? SubscriptionType,
    string? OAuthAccountEmail,
    string? OAuthAccountOrgName,
    DateTimeOffset? NextProbeAt);
