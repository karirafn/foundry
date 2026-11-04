using System.Xml.Linq;

using Foundry.Shared.Infrastructure.DataProtection;
using Foundry.Testing;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.DataProtection.LegacyDataProtectionTests;

public sealed class CreateProviderSharingKeyRing
{
    private const string LegacyName = "C:\\dev\\foundry\\src\\Foundry.WebApi\\";
    private const string Purpose = "Foundry.Test.Purpose";
    private const string Plaintext = "sécret-värde";

    [Fact]
    public void WhenKeyRingIsFileSystemBased_ReturnsProviderReadingLegacyCiphertext()
    {
        // Arrange
        using TemporaryKeyRing keys = new();
        string ciphertext = keys.ProviderFor(LegacyName).CreateProtector(Purpose).Protect(Plaintext);
        using ServiceProvider services = BuildServices(data => data.PersistKeysToFileSystem(keys.Directory));
        KeyManagementOptions hostKeyManagement = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        // Act
        IDataProtectionProvider? provider = LegacyDataProtection.CreateProviderSharingKeyRing(hostKeyManagement, LegacyName);

        // Assert
        provider.ShouldNotBeNull().CreateProtector(Purpose).Unprotect(ciphertext).ShouldBe(Plaintext);
    }

    [Fact]
    public void WhenHostHasNoXmlRepository_ReturnsNull()
    {
        // Arrange
        using ServiceProvider services = BuildServices(data => data.UseEphemeralDataProtectionProvider());
        KeyManagementOptions hostKeyManagement = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        // Act
        IDataProtectionProvider? provider = LegacyDataProtection.CreateProviderSharingKeyRing(hostKeyManagement, LegacyName);

        // Assert
        provider.ShouldBeNull();
    }

    [Fact]
    public void WhenXmlRepositoryIsNotFileSystemBased_ReturnsNull()
    {
        // Arrange
        KeyManagementOptions hostKeyManagement = new() { XmlRepository = new EphemeralXmlRepository() };

        // Act
        IDataProtectionProvider? provider = LegacyDataProtection.CreateProviderSharingKeyRing(hostKeyManagement, LegacyName);

        // Assert
        provider.ShouldBeNull();
    }

    private sealed class EphemeralXmlRepository : IXmlRepository
    {
        public IReadOnlyCollection<XElement> GetAllElements() => [];

        public void StoreElement(XElement element, string friendlyName)
        {
        }
    }

    private static ServiceProvider BuildServices(Action<IDataProtectionBuilder> configure)
    {
        ServiceCollection services = new();
        configure(services.AddDataProtection());

        return services.BuildServiceProvider();
    }
}
