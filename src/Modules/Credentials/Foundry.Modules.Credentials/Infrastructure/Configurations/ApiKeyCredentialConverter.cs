using System.Security.Cryptography;

using Foundry.Modules.Credentials.Domain.ValueObjects;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Modules.Credentials.Infrastructure.Configurations;

/// <summary>
/// Converts between <see cref="ApiKeyCredential"/> and a nullable encrypted TEXT column.
/// <list type="bullet">
/// <item><see cref="ApiKeyCredential.Present"/> — stored as the encrypted key value.</item>
/// <item><see cref="ApiKeyCredential.NotConfigured"/> — stored as NULL.</item>
/// <item><see cref="ApiKeyCredential.Unreadable"/> — only produced on read when decryption fails;
/// never written (the row value is left unchanged).</item>
/// </list>
/// A <see cref="CryptographicException"/> or <see cref="FormatException"/> while reading yields
/// <see cref="ApiKeyCredential.Unreadable"/> and logs a warning naming the column.
/// </summary>
internal sealed class ApiKeyCredentialConverter : ValueConverter<ApiKeyCredential?, string?>
{
    internal const string ColumnName = "claude_account.api_key";

    internal ApiKeyCredentialConverter(
        IDataProtectionProvider provider,
        ILogger<ApiKeyCredentialConverter>? logger = null)
        : base(
            credential => Encrypt(provider, credential),
            encrypted => Decrypt(
                provider,
                encrypted,
                logger ?? NullLogger<ApiKeyCredentialConverter>.Instance))
    {
    }

    private static string? Encrypt(IDataProtectionProvider provider, ApiKeyCredential? credential)
    {
        if (credential is not ApiKeyCredential.Present present)
        {
            return null;
        }

        IDataProtector protector = provider.CreateProtector(EncryptedStringConverter.ProtectorPurpose);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(present.Value);
        byte[] protectedBytes = protector.Protect(bytes);
        return Convert.ToBase64String(protectedBytes);
    }

    private static ApiKeyCredential? Decrypt(
        IDataProtectionProvider provider,
        string? encrypted,
        ILogger logger)
    {
        if (encrypted is null)
        {
            return new ApiKeyCredential.NotConfigured();
        }

        try
        {
            IDataProtector protector = provider.CreateProtector(EncryptedStringConverter.ProtectorPurpose);
            byte[] protectedBytes = Convert.FromBase64String(encrypted);
            byte[] bytes = protector.Unprotect(protectedBytes);
            string value = System.Text.Encoding.UTF8.GetString(bytes);
            return new ApiKeyCredential.Present(value);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to decrypt {Column}; the Data Protection key may have been rotated. Credential is unreadable.",
                ColumnName);
            return new ApiKeyCredential.Unreadable();
        }
        catch (FormatException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to decode {Column}; the stored value is not valid base-64. Credential is unreadable.",
                ColumnName);
            return new ApiKeyCredential.Unreadable();
        }
    }
}
