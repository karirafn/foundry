using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Settings.Infrastructure;

public sealed class EncryptedStringConverter : ValueConverter<string, string>
{
    private const string ProtectorPurpose = "Foundry.Settings.Encryption";

    public EncryptedStringConverter(IDataProtectionProvider provider)
        : base(
            value => Protect(provider.CreateProtector(ProtectorPurpose), value),
            protectedValue => Unprotect(provider.CreateProtector(ProtectorPurpose), protectedValue))
    {
    }

    private static string Protect(IDataProtector protector, string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        byte[] protectedBytes = protector.Protect(bytes);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(IDataProtector protector, string protectedValue)
    {
        try
        {
            byte[] protectedBytes = Convert.FromBase64String(protectedValue);
            byte[] bytes = protector.Unprotect(protectedBytes);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }
}
