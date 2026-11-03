namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Read model for an account. Tokens are never returned — only their status
/// (<c>present</c>, <c>absent</c> or <c>unreadable</c>).
/// </summary>
public sealed record CredentialSummary(
    Guid Id,
    string Name,
    string ProviderType,
    string BaseUrl,
    bool HasToken,
    string TokenStatus,
    IReadOnlyList<string> Namespaces,
    string BranchRulesTokenStatus);
