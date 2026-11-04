using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.Shared.Infrastructure.DataProtection;

public static partial class DataProtectionStartupExtensions
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
        ILogger<DataProtectedColumnReEncryptor> logger = services.GetRequiredService<ILogger<DataProtectedColumnReEncryptor>>();
        IDataProtectionProvider? legacy = CreateLegacyProvider(services, legacyName, logger);

        DataProtectedColumnReEncryptor reEncryptor = new(current, legacy, logger);

        return reEncryptor.ReEncryptAsync(dbContext, cancellationToken);
    }

    private static IDataProtectionProvider? CreateLegacyProvider(
        IServiceProvider services,
        string? legacyName,
        ILogger logger)
    {
        if (legacyName is null)
        {
            LogNoDistinctLegacyName(logger);
            return null;
        }

        IDataProtectionProvider? legacy = LegacyDataProtection.CreateProviderSharingKeyRing(
            services.GetRequiredService<IOptions<KeyManagementOptions>>().Value,
            legacyName);

        if (legacy is null)
        {
            LogKeyRepositoryNotFileSystemBased(logger);
        }

        return legacy;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Legacy Data Protection re-encryption is disabled: no distinct legacy application name was resolved (none configured or discovered, or it equals the current name)")]
    private static partial void LogNoDistinctLegacyName(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Legacy Data Protection re-encryption is disabled: the key repository is not file-system based, so the legacy provider cannot share the key ring")]
    private static partial void LogKeyRepositoryNotFileSystemBased(ILogger logger);
}
