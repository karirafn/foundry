namespace Foundry.Modules.Monitoring.Infrastructure;

internal sealed record TokenValidationResult(
    bool IsValid,
    bool IsAuthFailure,
    IReadOnlyList<string> MissingScopes,
    string? AccountName)
{
    public static TokenValidationResult Validated(IReadOnlyList<string> missingScopes, string? accountName) =>
        new(missingScopes.Count == 0, false, missingScopes, accountName);

    public static TokenValidationResult AuthFailure() =>
        new(false, true, [], null);
}
