using System.Diagnostics;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Accounts.Tokens;

/// <summary>
/// The identity a validated token authenticates as, and the expiry the provider reported for it, ready
/// to set on an account.
/// </summary>
internal sealed record ResolvedTokenIdentity(AccountName Name, TokenExpiry TokenExpiry);

/// <summary>
/// Turns a token-validation response into the identity an account takes from it. Shared by account
/// creation and token replacement so both accept and reject exactly the same responses.
/// </summary>
internal static class TokenIdentityResolution
{
    internal static Result<ResolvedTokenIdentity> Resolve(ValidateToken.Response response)
    {
        string? resolvedName = response.Kind switch
        {
            ValidateToken.Kinds.Authenticated
                when response.MissingScopes.Count == 0
                    && response.NotAllowed.Count == 0
                    && response.BroadReach.Count == 0 => response.AccountName,
            _ => null,
        };

        if (resolvedName is null)
        {
            return Result<ResolvedTokenIdentity>.Fail(RejectionFor(response));
        }

        return AccountName.Create(resolvedName) switch
        {
            Result<AccountName>.Success name => new ResolvedTokenIdentity(name.Value, response.Expiry),
            Result<AccountName>.Failure failure => Result<ResolvedTokenIdentity>.Fail(failure.Error),
            _ => throw new UnreachableException("Unhandled Result<AccountName> variant."),
        };
    }

    private static Error RejectionFor(ValidateToken.Response response) =>
        response.Kind switch
        {
            ValidateToken.Kinds.Authenticated when response.NotAllowed.Count > 0 || response.BroadReach.Count > 0 =>
                CredentialErrors.NonConformingToken,
            ValidateToken.Kinds.Authenticated => CredentialErrors.InvalidToken,
            ValidateToken.Kinds.AuthenticationFailed => CredentialErrors.InvalidToken,
            ValidateToken.Kinds.IdentityUnresolved => CredentialErrors.UnresolvedIdentity,
            ValidateToken.Kinds.WrongTokenKind => CredentialErrors.WrongTokenKind,
            ValidateToken.Kinds.ProviderMismatch => CredentialErrors.ProviderMismatch(response.DetectedProvider ?? string.Empty),
            _ => CredentialErrors.InvalidToken,
        };
}
