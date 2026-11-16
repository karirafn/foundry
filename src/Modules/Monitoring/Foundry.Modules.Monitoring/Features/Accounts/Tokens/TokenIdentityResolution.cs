using System.Diagnostics;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Accounts.Tokens;

/// <summary>The identity a validated token authenticates as, ready to set on an account.</summary>
internal sealed record ResolvedTokenIdentity(AccountName Name);

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
            ValidateToken.Kinds.Authenticated when response.MissingScopes.Count == 0 => response.AccountName,
            ValidateToken.Kinds.ScopesUnverifiable => response.AccountName,
            _ => null,
        };

        if (resolvedName is null)
        {
            return Result<ResolvedTokenIdentity>.Fail(RejectionFor(response));
        }

        return AccountName.Create(resolvedName) switch
        {
            Result<AccountName>.Success name => new ResolvedTokenIdentity(name.Value),
            Result<AccountName>.Failure failure => Result<ResolvedTokenIdentity>.Fail(failure.Error),
            _ => throw new UnreachableException("Unhandled Result<AccountName> variant."),
        };
    }

    private static Error RejectionFor(ValidateToken.Response response) =>
        response.Kind switch
        {
            ValidateToken.Kinds.Authenticated => CredentialErrors.InvalidToken,
            ValidateToken.Kinds.AuthenticationFailed => CredentialErrors.InvalidToken,
            ValidateToken.Kinds.IdentityUnresolved => CredentialErrors.UnresolvedIdentity,
            ValidateToken.Kinds.ScopesUnverifiable => CredentialErrors.UnresolvedIdentity,
            ValidateToken.Kinds.ProviderMismatch => CredentialErrors.ProviderMismatch(response.DetectedProvider ?? string.Empty),
            _ => CredentialErrors.InvalidToken,
        };
}
