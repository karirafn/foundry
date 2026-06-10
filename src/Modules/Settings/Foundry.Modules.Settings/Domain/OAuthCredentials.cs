namespace Foundry.Modules.Settings.Domain;

public sealed record OAuthCredentials(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    string SubscriptionType);
