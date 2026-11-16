using System.Security.Cryptography;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared.Infrastructure.DataProtection;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class ProviderTokenConverter : ValueConverter<ProviderToken?, string?>, IDataProtectedConverter
{
    private const string AccountTokenPurpose = "Foundry.Monitoring.Encryption";

    /// <summary>The purpose a Package Source Credential's token is encrypted under, apart from account tokens.</summary>
    internal const string PackageSourceCredentialPurpose = "Foundry.Monitoring.PackageSourceCredential";

    internal ProviderTokenConverter(IDataProtectionProvider provider)
        : this(provider, AccountTokenPurpose)
    {
    }

    internal ProviderTokenConverter(IDataProtectionProvider provider, string purpose)
        : base(
            token => token == null ? null : Protect(provider.CreateProtector(purpose), token),
            storedValue => storedValue == null ? null : Unprotect(
                provider.CreateProtector(purpose),
                storedValue))
    {
        ProtectorPurpose = purpose;
    }

    public string ProtectorPurpose { get; }

    private static string Protect(IDataProtector protector, ProviderToken token)
    {
        if (token is ProviderToken.Unreadable)
        {
            throw new InvalidOperationException(
                "Cannot persist an Unreadable token — it originated from a failed decryption and has no value to store.");
        }

        ProviderToken.Present present = (ProviderToken.Present)token;
        return DataProtectedText.Protect(protector, present.Value);
    }

    private static ProviderToken? Unprotect(IDataProtector protector, string storedValue)
    {
        try
        {
            return new ProviderToken.Present(DataProtectedText.Unprotect(protector, storedValue));
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
