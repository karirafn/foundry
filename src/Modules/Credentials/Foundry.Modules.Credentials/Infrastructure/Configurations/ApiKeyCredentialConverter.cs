using System.Security.Cryptography;

using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Shared.Infrastructure.DataProtection;

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
internal sealed class ApiKeyCredentialConverter : ValueConverter<ApiKeyCredential?, string?>, IDataProtectedConverter
{
    internal const string ColumnName = "claude_account.api_key";

    // This purpose string must remain stable — changing it renders every existing encrypted
    // api_key row unreadable, because Data Protection binds the key material to the purpose
    // at encryption time and rejects decryption attempts under any other value.
    private const string Purpose = "Foundry.Settings.Encryption";

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

    public string ProtectorPurpose => Purpose;

    private static string? Encrypt(IDataProtectionProvider provider, ApiKeyCredential? credential)
    {
        if (credential is not ApiKeyCredential.Present present)
        {
            return null;
        }

        return DataProtectedText.Protect(provider.CreateProtector(Purpose), present.Value);
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
            IDataProtector protector = provider.CreateProtector(Purpose);
            return new ApiKeyCredential.Present(DataProtectedText.Unprotect(protector, encrypted));
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
