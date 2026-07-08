namespace Foundry.Modules.Monitoring.Infrastructure;

internal sealed record TokenValidationResult
{
    private TokenValidationResult(bool isValid, bool isAuthFailure, IReadOnlyList<string> missingScopes, string? accountName)
    {
        IsValid = isValid;
        IsAuthFailure = isAuthFailure;
        MissingScopes = missingScopes;
        AccountName = accountName;
    }

    public bool IsValid { get; }

    public bool IsAuthFailure { get; }

    public IReadOnlyList<string> MissingScopes { get; }

    public string? AccountName { get; }

    public static TokenValidationResult Validated(IReadOnlyList<string> missingScopes, string? accountName) =>
        new(missingScopes.Count == 0, false, missingScopes, accountName);

    public static TokenValidationResult AuthFailure() =>
        new(false, true, [], null);
}
