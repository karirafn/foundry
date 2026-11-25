using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class AccountsDatabaseHelpers
{
    // Real GitHub/GitLab tokens are under 200 characters; 500 gives ample headroom
    // while preventing oversized values from bloating the encrypted column (max 2000 chars ciphertext).
    internal const int TokenMaxLength = 500;

    /// <summary>
    /// Maps the account token's <see cref="Credential.TokenUsability"/> to its wire string. An
    /// unreadable token reports <c>unreadable</c> even when the account is also Invalid.
    /// </summary>
    internal static string ToAccountTokenStatus(Credential credential)
    {
        TokenUsability usability = credential.TokenUsability;
        return usability switch
        {
            TokenUsability.Usable => TokenStatuses.Present,
            TokenUsability.Unreadable => TokenStatuses.Unreadable,
            TokenUsability.Invalid => TokenStatuses.Invalid,
            TokenUsability.Absent => TokenStatuses.Absent,
            _ => throw new UnreachableException($"Unhandled TokenUsability variant: {usability.GetType().Name}"),
        };
    }

    /// <summary>
    /// Projects a materialized <see cref="Credential"/> to its wire summary. The token value is
    /// never included; only its status. The token expiry
    /// is judged at <paramref name="now"/>.
    /// </summary>
    internal static CredentialSummary ToSummary(
        Credential credential,
        DateTimeOffset now,
        IReadOnlyList<DeniedRepositorySummary> deniedRepositories) =>
        new(
            credential.Id.Value,
            credential.Name.Value,
            credential is GitLabCredential ? ProviderTypes.GitLab : ProviderTypes.GitHub,
            credential.BaseUrl.Value.ToString(),
            credential.Token is not null,
            ToAccountTokenStatus(credential),
            credential.Namespaces.Select(n => n.Value).ToList(),
            credential.TokenUsability is TokenUsability.Invalid
                && credential.TokenValidity is TokenValidity.Invalid invalid
                    ? invalid.DetectedAt
                    : null,
            TokenExpirySummaries.From(credential.TokenExpiry, now),
            deniedRepositories,
            NonConformingTokenOf(credential));

    // Read from the stored token itself, so replacing the token clears it with nothing else to update.
    private static NonConformingTokenSummary? NonConformingTokenOf(Credential credential) =>
        credential is GitHubCredential
            && credential.TokenUsability is TokenUsability.Usable usable
            && GitHubTokenKind.WrongKindOf(usable.Token) is WrongTokenKind wrongKind
                ? new NonConformingTokenSummary(wrongKind.WireName(), [], [])
                : null;

    // SQLite error code 19 is SQLITE_CONSTRAINT (unique constraint violation).
    // The monitoring module does not reference the SQLite provider directly,
    // so we detect the violation via the exception message instead of SqliteException.SqliteErrorCode.
    // SQLite reports the constraint as "UNIQUE constraint failed: credential_namespaces.host, credential_namespaces.value"
    // or by the index name "ix_credential_namespaces_host_value", depending on the provider.
    internal static bool IsNamespaceDuplicateViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("ix_credential_namespaces_host_value", StringComparison.OrdinalIgnoreCase) == true
        || (ex.InnerException?.Message.Contains("credential_namespaces.host", StringComparison.OrdinalIgnoreCase) == true
            && ex.InnerException.Message.Contains("credential_namespaces.value", StringComparison.OrdinalIgnoreCase) == true);
}
