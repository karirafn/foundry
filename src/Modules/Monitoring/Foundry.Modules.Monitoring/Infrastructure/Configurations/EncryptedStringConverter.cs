using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class EncryptedStringConverter : ValueConverter<string?, string?>
{
    private const string ProtectorPurpose = "Foundry.Monitoring.Encryption";

    internal EncryptedStringConverter(IDataProtectionProvider provider, ILogger<EncryptedStringConverter>? logger = null)
        : base(
            value => value == null ? null : Protect(provider.CreateProtector(ProtectorPurpose), value),
            protectedValue => protectedValue == null ? null : Unprotect(
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
                "Failed to decrypt an account token; the Data Protection key may have been rotated. Returning empty string.");
            return string.Empty;
        }
    }
}
