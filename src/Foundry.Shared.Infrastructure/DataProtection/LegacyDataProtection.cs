using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Foundry.Shared.Infrastructure.DataProtection;

public static class LegacyDataProtection
{
    public static string? ResolveDiscriminator(string? configured, string? hostDefault, string currentName)
    {
        string? resolved = string.IsNullOrWhiteSpace(configured) ? hostDefault : configured.Trim();

        if (string.IsNullOrWhiteSpace(resolved) || string.Equals(resolved, currentName, StringComparison.Ordinal))
        {
            return null;
        }

        return resolved;
    }

    public static IDataProtectionProvider? CreateProviderSharingKeyRing(
        KeyManagementOptions hostKeyManagement,
        string legacyName)
    {
        if (hostKeyManagement.XmlRepository is not FileSystemXmlRepository repository)
        {
            return null;
        }

        return DataProtectionProvider.Create(
            repository.Directory,
            builder => builder
                .SetApplicationName(legacyName)
                .DisableAutomaticKeyGeneration());
    }
}
