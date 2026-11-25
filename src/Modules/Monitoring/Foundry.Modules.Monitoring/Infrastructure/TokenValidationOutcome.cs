using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Infrastructure;

internal abstract record TokenValidationOutcome
{
    private TokenValidationOutcome() { }

    internal sealed record AuthenticatedOutcome(
        string AccountName,
        IReadOnlyList<string> MissingScopes,
        TokenExpiry TokenExpiry) : TokenValidationOutcome;

    internal sealed record AuthenticationFailedOutcome : TokenValidationOutcome;

    internal sealed record ScopesUnverifiableOutcome(string AccountName, TokenExpiry TokenExpiry) : TokenValidationOutcome;

    /// <summary>The token authenticates, but it is a kind of token Foundry does not accept.</summary>
    internal sealed record WrongTokenKindOutcome(
        string AccountName,
        WrongTokenKind WrongKind,
        TokenExpiry TokenExpiry) : TokenValidationOutcome;

    internal sealed record IdentityUnresolvedOutcome : TokenValidationOutcome;

    internal sealed record ProviderMismatchOutcome(string DetectedProvider) : TokenValidationOutcome;

    internal static TokenValidationOutcome Authenticated(
        string accountName,
        TokenExpiry tokenExpiry,
        IReadOnlyList<string>? missingScopes = null) =>
        new AuthenticatedOutcome(accountName, missingScopes ?? [], tokenExpiry);

    internal static TokenValidationOutcome AuthenticationFailed() =>
        new AuthenticationFailedOutcome();

    internal static TokenValidationOutcome ScopesUnverifiable(string accountName, TokenExpiry tokenExpiry) =>
        new ScopesUnverifiableOutcome(accountName, tokenExpiry);

    internal static TokenValidationOutcome TokenOfWrongKind(
        string accountName,
        WrongTokenKind wrongKind,
        TokenExpiry tokenExpiry) =>
        new WrongTokenKindOutcome(accountName, wrongKind, tokenExpiry);

    internal static TokenValidationOutcome IdentityUnresolved() =>
        new IdentityUnresolvedOutcome();

    internal static TokenValidationOutcome ProviderMismatch(string detectedProvider) =>
        new ProviderMismatchOutcome(detectedProvider);
}
