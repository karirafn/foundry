using System.Text;

using Microsoft.AspNetCore.DataProtection;

namespace Foundry.Shared.Infrastructure.DataProtection;

/// <summary>
/// The storage framing every Data Protection column shares: base64 over the protected UTF-8 bytes.
/// Failures surface as <see cref="System.Security.Cryptography.CryptographicException"/> or
/// <see cref="FormatException"/> so each caller decides how an unreadable value materializes.
/// </summary>
public static class DataProtectedText
{
    public static string Protect(IDataProtector protector, string plaintext)
    {
        byte[] protectedBytes = protector.Protect(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(IDataProtector protector, string storedValue)
    {
        byte[] bytes = protector.Unprotect(Convert.FromBase64String(storedValue));
        return Encoding.UTF8.GetString(bytes);
    }
}
