namespace Foundry.Modules.Monitoring.Infrastructure;

internal abstract record TokenValidationOutcome
{
    private TokenValidationOutcome() { }

    internal sealed record AuthenticatedOutcome(
        string AccountName,
        IReadOnlyList<string> MissingScopes) : TokenValidationOutcome;

    internal sealed record AuthenticationFailedOutcome : TokenValidationOutcome;

    internal sealed record ScopesUnverifiableOutcome(string AccountName) : TokenValidationOutcome;

    internal sealed record IdentityUnresolvedOutcome : TokenValidationOutcome;

    internal sealed record ProviderMismatchOutcome(string DetectedProvider) : TokenValidationOutcome;

    internal static TokenValidationOutcome Authenticated(
        string accountName,
        IReadOnlyList<string>? missingScopes = null) =>
        new AuthenticatedOutcome(accountName, missingScopes ?? []);

    internal static TokenValidationOutcome AuthenticationFailed() =>
        new AuthenticationFailedOutcome();

    internal static TokenValidationOutcome ScopesUnverifiable(string accountName) =>
        new ScopesUnverifiableOutcome(accountName);

    internal static TokenValidationOutcome IdentityUnresolved() =>
        new IdentityUnresolvedOutcome();

    internal static TokenValidationOutcome ProviderMismatch(string detectedProvider) =>
        new ProviderMismatchOutcome(detectedProvider);
}
