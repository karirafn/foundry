using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Shared;

using BaseUrlVo = Foundry.Modules.Monitoring.Domain.ValueObjects.BaseUrl;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts.Tokens;

/// <summary>
/// Resolves a token to a structured <see cref="TokenResolution"/> that carries the
/// account name and derived namespaces on success, or a structured rejection on failure.
/// All validation and derivation logic runs here before any state mutation occurs.
/// </summary>
internal sealed class TokenAccountResolver(
    DbContext dbContext,
    IQueryHandler<ValidateToken.Query, ValidateToken.Response> validateToken,
    INamespaceDeriver namespaceDeriver)
{
    /// <summary>
    /// Validates <paramref name="token"/> against the provider, resolves the account name,
    /// derives namespaces, and guards against duplicate-account and fully-claimed-by-others
    /// failures — all before any state mutation.
    /// </summary>
    public async Task<TokenResolution> ResolveAsync(
        Credential credential,
        string token,
        BaseUrlVo baseUrl,
        bool isGitLab,
        CancellationToken cancellationToken)
    {
        Uri apiBaseUrl = isGitLab
            ? GitLabCredential.DeriveApiBaseUrl(baseUrl)
            : GitHubCredential.DeriveApiBaseUrl(baseUrl);

        string providerTypeForValidation = isGitLab ? ProviderTypes.GitLab : ProviderTypes.GitHub;

        ValidateToken.Query tokenQuery = new(token, apiBaseUrl, providerTypeForValidation);
        Result<ValidateToken.Response> tokenResult = await validateToken.HandleAsync(
            tokenQuery,
            cancellationToken);

        if (tokenResult is Result<ValidateToken.Response>.Failure tokenFailure)
        {
            return new TokenResolution.Rejected(tokenFailure.Error);
        }

        if (tokenResult is not Result<ValidateToken.Response>.Success { Value: ValidateToken.Response tokenResponse })
        {
            throw new UnreachableException(
                $"Token validation returned an unexpected result type: {tokenResult.GetType().Name}");
        }

        string? resolvedName = tokenResponse.Kind switch
        {
            ValidateToken.Kinds.Authenticated when tokenResponse.MissingScopes.Count == 0
                => tokenResponse.AccountName,
            ValidateToken.Kinds.ScopesUnverifiable
                => tokenResponse.AccountName,
            _ => null,
        };

        if (resolvedName is null)
        {
            Error kindError = tokenResponse.Kind switch
            {
                ValidateToken.Kinds.Authenticated => CredentialErrors.InvalidToken,
                ValidateToken.Kinds.AuthenticationFailed => CredentialErrors.InvalidToken,
                ValidateToken.Kinds.IdentityUnresolved => CredentialErrors.UnresolvedIdentity,
                ValidateToken.Kinds.ScopesUnverifiable => CredentialErrors.UnresolvedIdentity,
                ValidateToken.Kinds.ProviderMismatch =>
                    CredentialErrors.ProviderMismatch(tokenResponse.DetectedProvider ?? string.Empty),
                _ => CredentialErrors.InvalidToken,
            };
            return new TokenResolution.Rejected(kindError);
        }

        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            return new TokenResolution.Rejected(CredentialErrors.UnresolvedIdentity);
        }

        if (resolvedName.Length > AccountsDatabaseHelpers.AccountNameMaxLength || resolvedName.Any(char.IsControl))
        {
            return new TokenResolution.Rejected(CredentialErrors.UnresolvedIdentity);
        }

        return await DeriveAndGuardAsync(
            resolvedName,
            credential.Id.Value,
            apiBaseUrl,
            token,
            baseUrl,
            isGitLab,
            cancellationToken);
    }

    /// <summary>
    /// Derives namespaces for the token and guards against duplicate-account,
    /// fully-claimed-by-other-logins, and transient-unavailability failures.
    /// Returns <see cref="TokenResolution.Resolved"/> with the account name on success.
    /// </summary>
    private async Task<TokenResolution> DeriveAndGuardAsync(
        string resolvedName,
        Guid excludeCredentialId,
        Uri apiBaseUrl,
        string token,
        BaseUrlVo baseUrl,
        bool isGitLab,
        CancellationToken cancellationToken)
    {
        NamespaceDerivationOutcome outcome = await namespaceDeriver.DeriveAsync(
            apiBaseUrl,
            token,
            isGitLab,
            cancellationToken);

        // Branch on the DU variant — Unavailable is structurally distinct from Derived([]).
        // An empty derived set is a legitimate success; Unavailable is a transient failure.
        if (outcome is not NamespaceDerivationOutcome.Derived derived)
        {
            return new TokenResolution.Rejected(CredentialErrors.NamespaceDerivationUnavailable);
        }

        IReadOnlyCollection<Namespace> derivedNamespaces = derived.Namespaces;

        Dictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers =
            await dbContext.FindClaimedNamespacesAsync(
                baseUrl.Value.Host,
                excludingCredentialId: excludeCredentialId,
                cancellationToken);

        bool allDerivedClaimedByOthers = derivedNamespaces.All(ns => claimedByOthers.TryGetValue(ns.Value, out _));

        if (DuplicateAccount.Find(resolvedName, derivedNamespaces, claimedByOthers) is (string holderName, string sharedOwner))
        {
            // Reject only when rotation would strand the credential on zero namespace claims
            // because the sibling already covers the entire derived set.
            if (allDerivedClaimedByOthers)
            {
                return new TokenResolution.Rejected(
                    CredentialErrors.DuplicateAccount(holderName, sharedOwner));
            }
        }

        // Reject when the token's entire (non-empty) derived owner set is already claimed by
        // OTHER credentials — rotating would strand this account on zero namespace claims.
        bool derivedIsNonEmpty = derivedNamespaces.Count > 0;

        if (derivedIsNonEmpty && allDerivedClaimedByOthers)
        {
            List<NamespaceConflict> conflicts = derivedNamespaces
                .OrderBy(ns => ns.Value, StringComparer.Ordinal)
                .Select(ns =>
                {
                    claimedByOthers.TryGetValue(ns.Value, out (Guid HolderCredentialId, string HolderName) holder);
                    return new NamespaceConflict(ns.Value, holder.HolderCredentialId, holder.HolderName);
                })
                .ToList();

            Error claimedError = CredentialErrors.NamespaceClaimedElsewhere(conflicts);
            return new TokenResolution.ClaimedElsewhere(claimedError);
        }

        return new TokenResolution.Resolved(resolvedName, derivedNamespaces);
    }
}
