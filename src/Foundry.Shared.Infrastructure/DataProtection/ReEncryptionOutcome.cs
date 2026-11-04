using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;

namespace Foundry.Shared.Infrastructure.DataProtection;

/// <summary>
/// The decision for one stored Data Protection value: already readable under the current
/// identity, readable only under the legacy one (and so rewritten), or readable under neither.
/// The hierarchy is closed so consumers can switch exhaustively.
/// </summary>
public abstract record ReEncryptionOutcome
{
    private protected ReEncryptionOutcome()
    {
    }

    public static ReEncryptionOutcome Evaluate(string storedValue, IDataProtector current, IDataProtector? legacy)
    {
        if (TryUnprotect(current, storedValue, out _))
        {
            return new AlreadyCurrent();
        }

        if (legacy is not null && TryUnprotect(legacy, storedValue, out string plaintext))
        {
            return new Rewritten(DataProtectedText.Protect(current, plaintext));
        }

        return new Undecryptable();
    }

    private static bool TryUnprotect(IDataProtector protector, string storedValue, out string plaintext)
    {
        try
        {
            plaintext = DataProtectedText.Unprotect(protector, storedValue);
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            plaintext = string.Empty;
            return false;
        }
    }

    public sealed record AlreadyCurrent : ReEncryptionOutcome;

    public sealed record Rewritten(string NewStoredValue) : ReEncryptionOutcome;

    public sealed record Undecryptable : ReEncryptionOutcome;
}
