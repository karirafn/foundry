using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Entities;

internal static class CredentialErrors
{
    internal const string NotFoundCode = "Credential.NotFound";
    internal const string DuplicateNamespaceCode = "Credential.DuplicateNamespace";
    internal const string InvalidTokenCode = "Credential.InvalidToken";
    internal const string UnresolvedIdentityCode = "Credential.UnresolvedIdentity";

    internal static Error NotFound(CredentialId id) =>
        new(NotFoundCode, $"Credential with ID '{id.Value}' was not found.");

    internal static Error DuplicateNamespace(string ns) =>
        new(DuplicateNamespaceCode, $"Namespace '{ns}' is already claimed by another credential on this host.");

    internal static readonly Error InvalidToken =
        new(InvalidTokenCode, "The token is not valid or is missing required scopes.");

    internal static readonly Error UnresolvedIdentity =
        new(UnresolvedIdentityCode, "Could not resolve the account identity from the provider.");
}
