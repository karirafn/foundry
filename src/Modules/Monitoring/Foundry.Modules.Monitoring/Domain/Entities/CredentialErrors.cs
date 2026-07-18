using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Entities;

internal static class CredentialErrors
{
    internal const string NotFoundCode = "Credential.NotFound";
    internal const string DuplicateNameCode = "Credential.DuplicateName";
    internal const string InvalidTokenCode = "Credential.InvalidToken";
    internal const string UnresolvedIdentityCode = "Credential.UnresolvedIdentity";

    internal static Error NotFound(CredentialId id) =>
        new(NotFoundCode, $"Credential with ID '{id.Value}' was not found.");

    internal static Error DuplicateName(string name) =>
        new(DuplicateNameCode, $"A credential named '{name}' already exists.");

    internal static readonly Error InvalidToken =
        new(InvalidTokenCode, "The token is not valid or is missing required scopes.");

    internal static readonly Error UnresolvedIdentity =
        new(UnresolvedIdentityCode, "Could not resolve the account identity from the provider.");
}
