using Foundry.Shared.Infrastructure.DataProtection;
using Foundry.Testing;
using Foundry.UnitTests.Shared.Infrastructure.DataProtection.DataProtectedColumnReEncryptorTests;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.DataProtection.DataProtectionStartupExtensionsTests;

public sealed class ReEncryptLegacyCiphertextAsync : IAsyncDisposable
{
    private const string CurrentName = "Foundry";
    private const string LegacyName = "C:\\dev\\foundry\\src\\Foundry.WebApi\\";

    private readonly LegacyEncryptedDatabase _database = new();

    async ValueTask IAsyncDisposable.DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task WhenNoLegacyNameResolves_LogsWhyTheLegacyProviderIsDisabled()
    {
        // Arrange
        using TemporaryKeyRing keys = new();
        CapturingLogger<DataProtectedColumnReEncryptor> logger = new();

        // Act
        await RunAsync(keys, logger, configuredLegacyName: null, persistToFileSystem: true);

        // Assert
        logger.Entries.ShouldHaveSingleItem().Message.ShouldContain("no distinct legacy");
    }

    [Fact]
    public async Task WhenLegacyNameEqualsCurrent_LogsWhyTheLegacyProviderIsDisabled()
    {
        // Arrange
        using TemporaryKeyRing keys = new();
        CapturingLogger<DataProtectedColumnReEncryptor> logger = new();

        // Act
        await RunAsync(keys, logger, configuredLegacyName: CurrentName, persistToFileSystem: true);

        // Assert
        logger.Entries.ShouldHaveSingleItem().Message.ShouldContain("no distinct legacy");
    }

    [Fact]
    public async Task WhenKeyRepositoryIsNotFileSystemBased_LogsWhyTheLegacyProviderIsDisabled()
    {
        // Arrange
        using TemporaryKeyRing keys = new();
        CapturingLogger<DataProtectedColumnReEncryptor> logger = new();

        // Act
        await RunAsync(keys, logger, configuredLegacyName: LegacyName, persistToFileSystem: false);

        // Assert
        logger.Entries.ShouldHaveSingleItem().Message.ShouldContain("not file-system based");
    }

    [Fact]
    public async Task WhenLegacyProviderIsAvailable_LogsNothing()
    {
        // Arrange
        using TemporaryKeyRing keys = new();
        CapturingLogger<DataProtectedColumnReEncryptor> logger = new();

        // Act
        await RunAsync(keys, logger, configuredLegacyName: LegacyName, persistToFileSystem: true);

        // Assert
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenLegacyProviderIsDisabled_DoesNotLogTheConfiguredName()
    {
        // Arrange
        using TemporaryKeyRing keys = new();
        CapturingLogger<DataProtectedColumnReEncryptor> logger = new();

        // Act
        await RunAsync(keys, logger, configuredLegacyName: LegacyName, persistToFileSystem: false);

        // Assert
        logger.Entries.ShouldAllBe(entry => !entry.Message.Contains(LegacyName, StringComparison.Ordinal));
    }

    private async Task RunAsync(
        TemporaryKeyRing keys,
        CapturingLogger<DataProtectedColumnReEncryptor> logger,
        string? configuredLegacyName,
        bool persistToFileSystem)
    {
        ServiceCollection collection = new();
        IDataProtectionBuilder builder = collection.AddDataProtection().SetApplicationName(CurrentName);
        if (persistToFileSystem)
        {
            builder.PersistKeysToFileSystem(keys.Directory);
        }
        else
        {
            builder.UseEphemeralDataProtectionProvider();
        }

        collection.AddSingleton<ILogger<DataProtectedColumnReEncryptor>>(logger);
        using ServiceProvider services = collection.BuildServiceProvider();
        await using FoundryDbContext dbContext = _database.CreateDbContext(_database.Current);

        await services.ReEncryptLegacyCiphertextAsync(
            dbContext,
            configuredLegacyName,
            TestContext.Current.CancellationToken);
    }
}
