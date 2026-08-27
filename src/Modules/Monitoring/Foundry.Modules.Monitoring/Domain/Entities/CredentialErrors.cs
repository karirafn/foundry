using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Entities;

internal static class CredentialErrors
{
    internal const string NotFoundCode = "Credential.NotFound";
    internal const string DuplicateNamespaceCode = "Credential.DuplicateNamespace";
    internal const string InvalidTokenCode = "Credential.InvalidToken";
    internal const string UnresolvedIdentityCode = "Credential.UnresolvedIdentity";
    internal const string ProviderMismatchCode = "Credential.ProviderMismatch";
    internal const string MissingWritePermissionCode = "Credential.MissingWritePermission";
    internal const string NoNamespaceRepositoriesCode = "Credential.NoNamespaceRepositories";
    internal const string WriteAccessVerificationFailedCode = "Credential.WriteAccessVerificationFailed";
    internal const string DuplicateAccountCode = "Credential.DuplicateAccount";
    internal const string NamespaceDerivationUnavailableCode = "Credential.NamespaceDerivationUnavailable";
    internal const string RateLimitExhaustedCode = "Credential.RateLimitExhausted";

    internal static Error NotFound(CredentialId id) =>
        new(NotFoundCode, $"Credential with ID '{id.Value}' was not found.");

    internal static Error DuplicateNamespace(string host) =>
        new(DuplicateNamespaceCode, $"One or more namespaces derived for this token are already claimed by another credential on host '{host}'.");

    internal static Error MissingWritePermission(string permissionName) =>
        new(MissingWritePermissionCode, $"The token is missing the '{permissionName}' write permission required by Foundry.");

    internal static Error ProviderMismatch(string detectedProvider) =>
        new(ProviderMismatchCode,
            $"The token belongs to '{detectedProvider}'. Switch the provider selection to match the token.");

    internal static readonly Error InvalidToken =
        new(InvalidTokenCode, "The token is not valid or is missing required scopes.");

    internal static readonly Error UnresolvedIdentity =
        new(UnresolvedIdentityCode, "Could not resolve the account identity from the provider.");

    internal static readonly Error NoNamespaceRepositories =
        new(NoNamespaceRepositoriesCode,
            "No repositories were found under the derived namespace. " +
            "Either the token's resource owner does not cover the claimed namespace, " +
            "or the namespace has no repositories.");

    internal static readonly Error WriteAccessVerificationFailed =
        new(WriteAccessVerificationFailedCode,
            "Could not verify write access to the repository. Check your connection and try again.");

    internal static readonly Error NamespaceDerivationUnavailable =
        new(NamespaceDerivationUnavailableCode,
            "Could not derive namespaces from the provider — the provider may be temporarily unavailable. Try again.");

    internal static Error DuplicateAccount(string holderName, string sharedOwner) =>
        new(DuplicateAccountCode,
            $"Your '{holderName}' account already covers '{sharedOwner}'. Rotate that account's token instead.");

    internal static readonly Error RateLimitExhausted =
        new(RateLimitExhaustedCode,
            "GitHub API rate limit reached while verifying write access; wait and retry.");
}
