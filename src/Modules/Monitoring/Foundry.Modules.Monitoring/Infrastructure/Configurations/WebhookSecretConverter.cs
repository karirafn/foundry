using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

/// <summary>
/// Encrypts the per-repository webhook secret at rest with the same DataProtection mechanism as
/// provider tokens (<see cref="ProviderTokenConverter"/>), under its own purpose string. A value that
/// can no longer be decrypted (rotated key ring) materializes as <see langword="null"/>: deliveries then
/// fail verification until the operator reveals a freshly generated secret.
/// </summary>
internal sealed class WebhookSecretConverter : ValueConverter<string?, string?>
{
    private const string ProtectorPurpose = "Foundry.Monitoring.WebhookSecret";

    internal WebhookSecretConverter(IDataProtectionProvider provider)
        : base(
            secret => secret == null ? null : Protect(provider.CreateProtector(ProtectorPurpose), secret),
            storedValue => storedValue == null ? null : Unprotect(provider.CreateProtector(ProtectorPurpose), storedValue))
    {
    }

    private static string Protect(IDataProtector protector, string secret)
    {
        byte[] protectedBytes = protector.Protect(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(protectedBytes);
    }

    private static string? Unprotect(IDataProtector protector, string storedValue)
    {
        try
        {
            byte[] bytes = protector.Unprotect(Convert.FromBase64String(storedValue));
            return Encoding.UTF8.GetString(bytes);
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
