namespace Foundry.Modules.Monitoring.Infrastructure;

internal sealed record TokenValidationResult(
    bool IsValid,
    bool IsAuthFailure,
    bool ScopesVerified,
    IReadOnlyList<string> MissingScopes,
    string? AccountName)
{
    public static TokenValidationResult Validated(IReadOnlyList<string> missingScopes, string? accountName) =>
        new(missingScopes.Count == 0, false, true, missingScopes, accountName);

    public static TokenValidationResult AuthFailure() =>
        new(false, true, false, [], null);

    public static TokenValidationResult ScopesUnverifiable(string? accountName) =>
        new(false, false, false, [], accountName);
}
