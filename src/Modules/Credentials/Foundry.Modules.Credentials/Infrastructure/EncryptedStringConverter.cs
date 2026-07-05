using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Modules.Credentials.Infrastructure;

internal sealed class EncryptedStringConverter : ValueConverter<string, string>
{
    // Must match Foundry.Modules.Settings.Infrastructure.EncryptedStringConverter.ProtectorPurpose
    // so that the migration can copy ciphertext from global_settings verbatim without a decrypt
    // round-trip — both converters share the same Data Protection purpose string.
    internal const string ProtectorPurpose = "Foundry.Settings.Encryption";

    internal EncryptedStringConverter(IDataProtectionProvider provider, ILogger<EncryptedStringConverter>? logger = null)
        : base(
            value => Protect(provider.CreateProtector(ProtectorPurpose), value),
            protectedValue => Unprotect(
                provider.CreateProtector(ProtectorPurpose),
                protectedValue,
                logger ?? NullLogger<EncryptedStringConverter>.Instance))
    {
    }

    private static string Protect(IDataProtector protector, string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        byte[] protectedBytes = protector.Protect(bytes);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(IDataProtector protector, string protectedValue, ILogger logger)
    {
        try
        {
            byte[] protectedBytes = Convert.FromBase64String(protectedValue);
            byte[] bytes = protector.Unprotect(protectedBytes);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException ex)
        {
            // Data Protection key may have been rotated — the stored value cannot be decrypted.
            logger.LogWarning(
                ex,
                "Failed to decrypt a credentials value; the Data Protection key may have been rotated. Returning empty string.");
            return string.Empty;
        }
    }
}
