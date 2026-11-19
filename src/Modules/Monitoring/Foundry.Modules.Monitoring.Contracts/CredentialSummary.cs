namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Read model for an account. Tokens are never returned — only their status
/// (<c>present</c>, <c>absent</c>, <c>unreadable</c> or <c>invalid</c>).
/// <see cref="TokenInvalidSince"/> is when the provider's refusal of the account token was first
/// confirmed; it is null unless <see cref="TokenStatus"/> is <c>invalid</c>. <see cref="TokenExpiry"/> is the
/// account token's expiry as last read from the provider, judged when the summary was built.
/// <see cref="DeniedRepositories"/> lists the monitored repositories resolving to this account whose
/// write-probe verdict is <c>Denied</c>.
/// </summary>
public sealed record CredentialSummary(
    Guid Id,
    string Name,
    string ProviderType,
    string BaseUrl,
    bool HasToken,
    string TokenStatus,
    IReadOnlyList<string> Namespaces,
    DateTimeOffset? TokenInvalidSince,
    TokenExpirySummary TokenExpiry,
    IReadOnlyList<DeniedRepositorySummary> DeniedRepositories);
