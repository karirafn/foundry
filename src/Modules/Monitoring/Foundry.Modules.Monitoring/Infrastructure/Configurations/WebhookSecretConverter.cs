using System.Security.Cryptography;

using Foundry.Shared.Infrastructure.DataProtection;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

/// <summary>
/// Encrypts the per-repository webhook secret at rest with the same DataProtection mechanism as
/// provider tokens (<see cref="ProviderTokenConverter"/>), under its own purpose string. A value that
/// can no longer be decrypted (rotated key ring) materializes as <see langword="null"/>: deliveries then
/// fail verification until the operator reveals a freshly generated secret.
/// </summary>
internal sealed class WebhookSecretConverter : ValueConverter<string?, string?>, IDataProtectedConverter
{
    private const string Purpose = "Foundry.Monitoring.WebhookSecret";

    internal WebhookSecretConverter(IDataProtectionProvider provider)
        : base(
            secret => secret == null ? null : Protect(provider.CreateProtector(Purpose), secret),
            storedValue => storedValue == null ? null : Unprotect(provider.CreateProtector(Purpose), storedValue))
    {
    }

    public string ProtectorPurpose => Purpose;

    private static string Protect(IDataProtector protector, string secret)
    {
        return DataProtectedText.Protect(protector, secret);
    }

    private static string? Unprotect(IDataProtector protector, string storedValue)
    {
        try
        {
            return DataProtectedText.Unprotect(protector, storedValue);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
