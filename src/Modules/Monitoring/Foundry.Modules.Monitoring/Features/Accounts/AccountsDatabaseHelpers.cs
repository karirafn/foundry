using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class AccountsDatabaseHelpers
{
    internal const int AccountNameMaxLength = 200;

    // Real GitHub/GitLab tokens are under 200 characters; 500 gives ample headroom
    // while preventing oversized values from bloating the encrypted column (max 2000 chars ciphertext).
    internal const int TokenMaxLength = 500;

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
