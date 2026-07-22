namespace Foundry.Modules.Monitoring.Infrastructure;

internal sealed record TokenValidationResult(
    bool IsValid,
    bool IsAuthFailure,
    bool ScopesVerified,
    IReadOnlyList<string> MissingScopes,
    string? AccountName)
{
    public static TokenValidationResult Validated(IReadOnlyList<string> missingScopes, string? accountName) =>
        new(IsValid: missingScopes.Count == 0, IsAuthFailure: false, ScopesVerified: true, MissingScopes: missingScopes, AccountName: accountName);

    public static TokenValidationResult AuthFailure() =>
        new(IsValid: false, IsAuthFailure: true, ScopesVerified: false, MissingScopes: [], AccountName: null);

    public static TokenValidationResult ScopesUnverifiable(string? accountName) =>
        new(IsValid: false, IsAuthFailure: false, ScopesVerified: false, MissingScopes: [], AccountName: accountName);
}
