using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class AccountsDatabaseHelpers
{
    internal const int AccountNameMaxLength = 200;

    // SQLite error code 19 is SQLITE_CONSTRAINT (unique constraint violation).
    // The monitoring module does not reference the SQLite provider directly,
    // so we detect the violation via the exception message instead of SqliteException.SqliteErrorCode.
    // We match the specific index name columns to distinguish account name duplicates from
    // any other unique constraint violations on the accounts table.
    internal static bool IsAccountNameDuplicateViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("ix_accounts_base_url_name", StringComparison.OrdinalIgnoreCase) == true
        || (ex.InnerException?.Message.Contains("accounts.base_url", StringComparison.OrdinalIgnoreCase) == true
            && ex.InnerException.Message.Contains("accounts.name", StringComparison.OrdinalIgnoreCase) == true);
}
