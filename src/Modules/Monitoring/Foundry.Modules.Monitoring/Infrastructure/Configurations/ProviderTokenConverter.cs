using System.Security.Cryptography;

using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class ProviderTokenConverter : ValueConverter<ProviderToken?, string?>
{
    private const string ProtectorPurpose = "Foundry.Monitoring.Encryption";

    internal ProviderTokenConverter(IDataProtectionProvider provider)
        : base(
            token => token == null ? null : Protect(provider.CreateProtector(ProtectorPurpose), token),
            storedValue => storedValue == null ? null : Unprotect(
                provider.CreateProtector(ProtectorPurpose),
                storedValue))
    {
    }

    private static string Protect(IDataProtector protector, ProviderToken token)
    {
        if (token is ProviderToken.Unreadable)
        {
            throw new InvalidOperationException(
                "Cannot persist an Unreadable token — it originated from a failed decryption and has no value to store.");
        }

        ProviderToken.Present present = (ProviderToken.Present)token;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(present.Value);
        byte[] protectedBytes = protector.Protect(bytes);
        return Convert.ToBase64String(protectedBytes);
    }

    private static ProviderToken? Unprotect(IDataProtector protector, string storedValue)
    {
        try
        {
            byte[] protectedBytes = Convert.FromBase64String(storedValue);
            byte[] bytes = protector.Unprotect(protectedBytes);
            return new ProviderToken.Present(System.Text.Encoding.UTF8.GetString(bytes));
        }
        catch (CryptographicException)
        {
            return new ProviderToken.Unreadable();
        }
        catch (FormatException)
        {
            return new ProviderToken.Unreadable();
        }
    }
}
