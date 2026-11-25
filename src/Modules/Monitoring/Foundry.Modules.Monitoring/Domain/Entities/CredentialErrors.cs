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
    internal const string WrongTokenKindCode = "Credential.WrongTokenKind";

    internal static Error NotFound(CredentialId id) =>
        new(NotFoundCode, $"Credential with ID '{id.Value}' was not found.");

    internal static Error DuplicateNamespace(string host) =>
        new(DuplicateNamespaceCode, $"One or more namespaces derived for this token are already claimed by another credential on host '{host}'.");

    internal static Error MissingWritePermission(IReadOnlyList<string> permissionLabels) =>
        new(MissingWritePermissionCode, permissionLabels.Count == 1
            ? $"The token is missing the '{permissionLabels[0]}' permission required by Foundry."
            : $"The token is missing the {QuotedList(permissionLabels)} permissions required by Foundry.");

    private static string QuotedList(IReadOnlyList<string> labels)
    {
        string[] quoted = [.. labels.Select(label => $"'{label}'")];
        return quoted.Length <= 1
            ? string.Join(string.Empty, quoted)
            : $"{string.Join(", ", quoted[..^1])} and {quoted[^1]}";
    }

    internal static Error ProviderMismatch(string detectedProvider) =>
        new(ProviderMismatchCode,
            $"The token belongs to '{detectedProvider}'. Switch the provider selection to match the token.");

    internal static readonly Error InvalidToken =
        new(InvalidTokenCode, "The token is not valid or is missing required scopes.");

    internal static readonly Error WrongTokenKind =
        new(WrongTokenKindCode, "Foundry needs a fine-grained personal access token; this token is of another kind.");

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

    internal const string NamespaceClaimedElsewhereCode = "Credential.NamespaceClaimedElsewhere";

    internal static Error NamespaceClaimedElsewhere(IReadOnlyList<NamespaceConflict> conflicts)
    {
        string holderList = string.Join(
            ", ",
            conflicts.Select(c => $"{c.Namespace} (held by {c.HolderName})"));
        return new Error(
            NamespaceClaimedElsewhereCode,
            $"Cannot rotate token: all derived namespaces are claimed by other accounts — {holderList}.");
    }
}
