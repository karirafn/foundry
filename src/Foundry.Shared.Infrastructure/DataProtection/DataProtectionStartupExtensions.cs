using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.Shared.Infrastructure.DataProtection;

public static class DataProtectionStartupExtensions
{
    public static Task ReEncryptLegacyCiphertextAsync(
        this IServiceProvider services,
        DbContext dbContext,
        string? configuredLegacyName,
        CancellationToken cancellationToken)
    {
        IDataProtectionProvider current = services.GetRequiredService<IDataProtectionProvider>();
        string currentName = services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator
            ?? string.Empty;
        string? legacyName = LegacyDataProtection.ResolveDiscriminator(
            configuredLegacyName,
            services.GetApplicationUniqueIdentifier(),
            currentName);
        IDataProtectionProvider? legacy = legacyName is null
            ? null
            : LegacyDataProtection.CreateProviderSharingKeyRing(
                services.GetRequiredService<IOptions<KeyManagementOptions>>().Value,
                legacyName);

        DataProtectedColumnReEncryptor reEncryptor = new(
            current,
            legacy,
            services.GetRequiredService<ILogger<DataProtectedColumnReEncryptor>>());

        return reEncryptor.ReEncryptAsync(dbContext, cancellationToken);
    }
}
